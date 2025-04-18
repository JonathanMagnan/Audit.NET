﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Audit.Core;
using Audit.IntegrationTest;
using Audit.OpenSearch.Providers;

using NUnit.Framework;

using OpenSearch.Client;

namespace Audit.OpenSearch.UnitTest
{
    public class OpenSearchTests
    {
        [SetUp]
        public void Setup()
        {
            Audit.Core.Configuration.Reset();
        }

        [Test]
        public void Test_OpenSearchDataProvider_FluentApi()
        {
            var x = new OpenSearchDataProvider(_ => _
                .Client(new OpenSearchClient(new Uri("http://server/")))
                .Id(ev => "id")
                .Index("ix"));

            // Assert.That((x.Settings.NodePool.Nodes.First().Uri.ToString()), Is.EqualTo("http://server/"));
            Assert.That(x.IdBuilder.Invoke(null).Equals(new Id("id")), Is.True);
            Assert.That(x.Index.GetDefault(), Is.EqualTo((IndexName)"ix"));
        }

        [Test]
        [Category("Integration")]
        [Category("OpenSearch")]
        public void Test_OpenSearch_HappyPath()
        {
            var dataProvider = new OpenSearchDataProvider(new OpenSearchClient(new Uri(AzureSettings.OpenSearchUrl)));
            var indexName = "auditevent_order";
            
            var guids = new List<string>();
            dataProvider.Index = (IndexName)indexName;
            dataProvider.IdBuilder = ev => { var g = Guid.NewGuid().ToString(); guids.Add(g); return g; };

            Audit.Core.Configuration.Setup()
                .UseCustomProvider(dataProvider)
                .WithCreationPolicy(EventCreationPolicy.InsertOnStartReplaceOnEnd)
                .ResetActions();

            var order = new Order()
            {
                Id = 1,
                Status = "Created"
            };
            
            using (var scope = new AuditScopeFactory().Create("eventType", () => order, new { MyCustomField = "value" }, null, null))
            {
                order.Status = "Updated";
            }

            var elasticClient = dataProvider.GetClient();
            elasticClient.Indices.Refresh(indexName);

            var evLoad = dataProvider.GetEvent(new OpenSearchAuditEventId() { Id = guids[0], Index = indexName });
            
            Assert.That(evLoad, Is.Not.Null);
            Assert.That(guids.Count, Is.EqualTo(1));
            Assert.That(evLoad.CustomFields["MyCustomField"].ToString(), Is.EqualTo("value"));
        }

        [Test]
        [Category("Integration")]
        [Category("OpenSearch")]
        public async Task Test_OpenSearch_HappyPath_Async()
        {
            var dataProvider = new OpenSearchDataProvider(c => c.Client(new ConnectionSettings(new Uri(AzureSettings.OpenSearchUrl))));
            var indexName = "auditevent_order";

            var guids = new List<string>();
            dataProvider.Index = (IndexName)indexName;
            dataProvider.IdBuilder = ev => { var g = Guid.NewGuid().ToString(); guids.Add(g); return g; };

            Audit.Core.Configuration.Setup()
                .UseCustomProvider(dataProvider)
                .WithCreationPolicy(Core.EventCreationPolicy.InsertOnStartReplaceOnEnd)
                .ResetActions();

            var order = new Order()
            {
                Id = 1,
                Status = "Created"
            };

            using (var scope = await new AuditScopeFactory().CreateAsync("eventType", () => order, new { MyCustomField = "value" }, null, null))
            {
                order.Status = "Updated";
            }

            var elasticClient = dataProvider.GetClient();
            await elasticClient.Indices.RefreshAsync(indexName);

            var evLoad = await dataProvider.GetEventAsync(new OpenSearchAuditEventId() { Id = guids[0], Index = indexName });

            Assert.That(evLoad, Is.Not.Null);
            Assert.That(guids.Count, Is.EqualTo(1));
            Assert.That(evLoad.CustomFields["MyCustomField"].ToString(), Is.EqualTo("value"));
        }

        [Test]
        [Category("Integration")]
        [Category("OpenSearch")]
        public void Test_OpenSearch_AutoGeneratedId()
        {
            var dataProvider = new OpenSearchDataProvider(c => c.Client(new OpenSearchClient(new ConnectionSettings(new Uri(AzureSettings.OpenSearchUrl)))));
            var indexName = "auto_" + new Random().Next(10000, 99999);

            dataProvider.Index = (IndexName)indexName;
            dataProvider.IdBuilder = ev => null;

            Audit.Core.Configuration.Setup()
                .UseCustomProvider(dataProvider)
                .WithCreationPolicy(Core.EventCreationPolicy.InsertOnStartReplaceOnEnd)
                .ResetActions();

            var sb = "init";


            using (var scope = new AuditScopeFactory().Create("eventType", () => sb, new { MyCustomField = "value" }, null, null))
            {
                sb += "-end";
            }

            var elasticClient = dataProvider.GetClient();
            elasticClient.Indices.Refresh(indexName);

            var results = elasticClient.Search<AuditEvent>(new SearchRequest(indexName));
            var evResult = results.Documents.FirstOrDefault();
            if (evResult != null)
            {
                elasticClient.Delete(new DeleteRequest(results.Hits.First().Index, results.Hits.First().Id));
            }

            Assert.That(evResult, Is.Not.Null);
            Assert.That(results.Documents.Count, Is.EqualTo(1));
            Assert.That(evResult.Target.Old.ToString(), Is.EqualTo("init"));
            Assert.That(evResult.Target.New.ToString(), Is.EqualTo("init-end"));
            Assert.That(evResult.CustomFields["MyCustomField"]?.ToString(), Is.EqualTo("value"));
        }

        [Test]
        [Category("Integration")]
        [Category("OpenSearch")]
        public async Task Test_OpenSearch_AutoGeneratedId_Async()
        {
            var dataProvider = new OpenSearchDataProvider(c => c.Client(new Uri(AzureSettings.OpenSearchUrl)));
            var indexName = "auto_" + new Random().Next(10000, 99999);

            dataProvider.Index = (IndexName)indexName;
            dataProvider.IdBuilder = ev => null;

            Audit.Core.Configuration.Setup()
                .UseCustomProvider(dataProvider)
                .WithCreationPolicy(Core.EventCreationPolicy.InsertOnStartReplaceOnEnd)
                .ResetActions();

            var sb = "init";


            using (var scope = await new AuditScopeFactory().CreateAsync("eventType", () => sb, new { MyCustomField = "value" }, null, null))
            {
                sb += "-end";
            }

            var elasticClient = dataProvider.GetClient();
            await elasticClient.Indices.RefreshAsync(indexName);

            var results = await elasticClient.SearchAsync<Core.AuditEvent>(new SearchRequest(indexName));
            var evResult = results.Documents.FirstOrDefault();
            if (evResult != null)
            {
                await elasticClient.DeleteAsync(new DeleteRequest(results.Hits.First().Index, results.Hits.First().Id));
            }

            Assert.That(evResult, Is.Not.Null);
            Assert.That(results.Documents.Count, Is.EqualTo(1));
            Assert.That(evResult.Target.Old.ToString(), Is.EqualTo("init"));
            Assert.That(evResult.Target.New.ToString(), Is.EqualTo("init-end"));
            Assert.That(evResult.CustomFields["MyCustomField"]?.ToString(), Is.EqualTo("value"));
        }

        [Test]
        [Category("Integration")]
        [Category("OpenSearch")]
        public void Test_OpenSearch_Polymorphic_Serialization()
        {
            var indexName = "auto_" + new Random().Next(10000, 99999);
            var dp = new OpenSearchDataProvider(c => c.Client(new Uri(AzureSettings.OpenSearchUrl)).Index(indexName));

            Audit.Core.Configuration.Setup().UseNullProvider();

            var ev = new CustomAuditEvent()
            {
                CustomProperty = "test"
            };

            var scope = AuditScope.Create(new AuditScopeOptions() { AuditEvent = ev });
            scope.SetCustomField("CustomField", "value");
            scope.Discard();

            var id = dp.InsertEvent(ev) as OpenSearchAuditEventId;

            var result = dp.GetEvent<CustomAuditEvent>(id);

            var elasticClient = dp.GetClient();
            elasticClient.Indices.Delete(new DeleteIndexRequest(indexName));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.CustomFields.Count, Is.EqualTo(1));
            Assert.That(result.CustomFields["CustomField"].ToString(), Is.EqualTo("value"));
            Assert.That(result.CustomProperty, Is.EqualTo("test"));
        }

        [Test]
        [Category("Integration")]
        [Category("OpenSearch")]
        public async Task Test_OpenSearch_Polymorphic_SerializationAsync()
        {
            var indexName = "auto_" + new Random().Next(10000, 99999);
            var dp = new OpenSearchDataProvider(c => c.Client(new Uri(AzureSettings.OpenSearchUrl)).Index(indexName));

            Audit.Core.Configuration.Setup().UseNullProvider();

            var ev = new CustomAuditEvent()
            {
                CustomProperty = "test"
            };

            var scope = await AuditScope.CreateAsync(new AuditScopeOptions() { AuditEvent = ev });
            scope.SetCustomField("CustomField", "value");
            scope.Discard();

            var id = (await dp.InsertEventAsync(ev)) as OpenSearchAuditEventId;
            ev.CustomProperty = "updated";
            await dp.ReplaceEventAsync(id, ev);

            var result = await dp.GetEventAsync<CustomAuditEvent>(id);

            var elasticClient = dp.GetClient();
            await elasticClient.Indices.DeleteAsync(new DeleteIndexRequest(indexName));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.CustomFields.Count, Is.EqualTo(1));
            Assert.That(result.CustomFields["CustomField"].ToString(), Is.EqualTo("value"));
            Assert.That(result.CustomProperty, Is.EqualTo("updated"));
        }
    }

    public class CustomAuditEvent : AuditEvent
    {
        public string CustomProperty { get; set; }
    }

    public class Order
    {
        public virtual long Id { get; set; }
        public virtual string Number { get; set; }
        public virtual string Status { get; set; }
    }
}
