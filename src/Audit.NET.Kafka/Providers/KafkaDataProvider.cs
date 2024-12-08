﻿using Audit.Core;
using Confluent.Kafka;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Audit.Kafka.Providers
{
    /// <summary>
    /// Apache Kafka data provider
    /// </summary>
    public class KafkaDataProvider : KafkaDataProvider<Null>
    {
        public KafkaDataProvider(ProducerConfig config) : base(config) { }

        public KafkaDataProvider(Action<Configuration.IKafkaProviderConfigurator<Null>> config) : base(config) { }
    }

    /// <summary>
    /// Apache Kafka data provider (keyed messages)
    /// </summary>
    public class KafkaDataProvider<TKey> : AuditDataProvider
    {
        private readonly ProducerBuilder<TKey, AuditEvent> _producerBuilder;
        private IProducer<TKey, AuditEvent> _producer;

        private static readonly object ProducerLocker = new object();

        /// <summary>
        /// Kafka Topic selector to be used. Default is "audit-topic".
        /// </summary>
        public Setting<string> Topic { get; set; }

        /// <summary>
        /// Partition selector to be used as a function of the audit event. Default or NULL means any partition
        /// </summary>
        public Setting<int?> Partition { get; set; }

        /// <summary>
        /// Key selector. Optional to use keyed messages. Return the key to be used for a given audit event.
        /// </summary>
        public Func<AuditEvent, TKey> KeySelector { get; set; }

        /// <summary>
        /// Headers Selector. Optional to use message headers. Return the message headers to be used for a given audit event.
        /// </summary>
        public Func<AuditEvent, Headers> HeadersSelector { get; set; }

        /// <summary>
        /// Key serializer. Optional when using keyed messages and a custom serializer for the key is needed.
        /// </summary>
        public ISerializer<TKey> KeySerializer { get; set; }

        /// <summary>
        /// Custom AuditEvent serializer. By default, the audit event is JSON serialized + UTF8 encoded.
        /// </summary>
        public ISerializer<AuditEvent> AuditEventSerializer { get; set; }

        /// <summary>
        /// Gets or sets the result handler action. An action to be called for each kafka response
        /// </summary>
        /// <value>An action to be called for each kafka response.</value>
        public Action<DeliveryResult<TKey, AuditEvent>> ResultHandler { get; set; }

        /// <summary>
        /// Gets or sets the producer builder action. An action to be called before building the producer.
        /// </summary>
        /// <value>The producer builder extra configuration.</value>
        public Action<ProducerBuilder<TKey, AuditEvent>> ProducerBuilderAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="KafkaDataProvider{TKey}"/> class.
        /// </summary>
        /// <param name="config">The configuration fluent API.</param>
        public KafkaDataProvider(Action<Configuration.IKafkaProviderConfigurator<TKey>> config)
        {
            var kafkaConfig = new Configuration.KafkaProviderConfigurator<TKey>();
            if (config != null)
            {
                config.Invoke(kafkaConfig);
                _producerBuilder = new ProducerBuilder<TKey, AuditEvent>(kafkaConfig._producerConfig);
                Topic = kafkaConfig._topic;
                Partition = kafkaConfig._partition;
                KeySelector = kafkaConfig._keySelector;
                KeySerializer = kafkaConfig._keySerializer;
                AuditEventSerializer = kafkaConfig._auditEventSerializer;
                ResultHandler = kafkaConfig._resultHandler;
                ProducerBuilderAction = kafkaConfig._producerBuilderAction;
                HeadersSelector = kafkaConfig._headersSelector;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KafkaDataProvider{TKey}"/> class.
        /// </summary>
        /// <param name="producerConfig">The producer configuration.</param>
        public KafkaDataProvider(ProducerConfig producerConfig)
        {
            _producerBuilder = new ProducerBuilder<TKey, AuditEvent>(producerConfig);
        }

        private void EnsureProducer()
        {
            if (_producer == null)
            {
                lock(ProducerLocker)
                {
                    if (_producer == null)
                    {
                        if (KeySerializer != null)
                        {
                            _producerBuilder.SetKeySerializer(KeySerializer);
                        }
                        _producerBuilder.SetValueSerializer(AuditEventSerializer ?? new DefaultJsonSerializer<AuditEvent>());
                        // allow extra configuration from the client
                        ProducerBuilderAction?.Invoke(_producerBuilder);
                        _producer = _producerBuilder.Build();
                    }
                }
            }
            
        }

        public override object InsertEvent(AuditEvent auditEvent)
        {
            EnsureProducer();
            return Produce(auditEvent);
        }

        public override async Task<object> InsertEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            EnsureProducer();
            return await ProduceAsync(auditEvent, cancellationToken);
        }

        public override void ReplaceEvent(object eventId, AuditEvent auditEvent)
        {
            EnsureProducer();
            Produce(auditEvent);
        }

        public override async Task ReplaceEventAsync(object eventId, AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            EnsureProducer();
            await ProduceAsync(auditEvent, cancellationToken);
        }

        public override T GetEvent<T>(object eventId)
        {
            throw new NotImplementedException();
        }

        public override Task<T> GetEventAsync<T>(object eventId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the topic and partition for the given audit event. Override this method to customize the topic and partition selection.
        /// </summary>
        public virtual TopicPartition GetTopicPartition(AuditEvent auditEvent)
        {
            var topic = Topic.GetValue(auditEvent) ?? "audit-topic";
            var partitionIndex = Partition.GetValue(auditEvent);
            var partition = partitionIndex.HasValue ? new Partition(partitionIndex.Value) : Confluent.Kafka.Partition.Any;
            return new TopicPartition(topic, partition);
        }

        /// <summary>
        /// Creates a Kafka message from an AuditEvent. Override this method to customize the message creation.
        /// </summary>
        public virtual Message<TKey, AuditEvent> CreateMessage(AuditEvent auditEvent)
        {
            var key = KeySelector == null ? default : KeySelector.Invoke(auditEvent);
            var headers = HeadersSelector?.Invoke(auditEvent);
            return new Message<TKey, AuditEvent> { Key = key, Value = auditEvent, Headers = headers };
        }

        private TKey Produce(AuditEvent auditEvent)
        {
            var message = CreateMessage(auditEvent);
            var topic = GetTopicPartition(auditEvent);
            var result = _producer.ProduceAsync(topic, message).GetAwaiter().GetResult();
            ResultHandler?.Invoke(result);
            return result.Key;
        }

        private async Task<TKey> ProduceAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            var message = CreateMessage(auditEvent);
            var topic = GetTopicPartition(auditEvent);
            var result = await _producer.ProduceAsync(topic, message, cancellationToken);
            ResultHandler?.Invoke(result);
            return result.Key;
        }
    }
}
