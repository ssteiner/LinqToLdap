﻿using System.Collections.Generic;
using LinqToLdap.Tests.TestSupport.ExtensionMethods;
using LinqToLdap.Visitors;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpTestsEx;

namespace LinqToLdap.Tests.Visitors
{
    [TestClass]
    public class SelectProjectorTest
    {
        private Dictionary<string, string> _properties;
        private SelectProjector _projector;

        [TestInitialize]
        public void SetUp()
        {
            _properties = new Dictionary<string, string>
                              {
                                  {"Property1", "x"},
                                  {"Property2", "y"},
                                  {"Property3", "z"},
                                  {"Property4", "a"},
                                  {"Property5", "b"},
                                  {"Property6", "c"}
                              };

            _projector = new SelectProjector(_properties);
        }

        [TestMethod]
        public void ProjectProperties_ProjectFullInstance_ReturnsAllProperties()
        {
            //prepare
            var instance = new QueryTranslatorTestClass();
            var expression = instance.CreateExpression(t => t);

            //act
            var projection = _projector.ProjectProperties(expression);

            //assert
            projection.ReturnType.Should().Be.EqualTo(typeof(QueryTranslatorTestClass));
            projection.SelectedProperties.Should().Have.SameSequenceAs(_properties);
            projection.Projection.DynamicInvoke(instance).Should().Be.SameInstanceAs(instance);
        }

        [TestMethod]
        public void ProjectProperties_ProjectNewInstance_ReturnsProjectedProperties()
        {
            //prepare
            var instance = new QueryTranslatorTestClass { Property1 = "p1" };
            var expression = instance.CreateExpression(t => new QueryTranslatorTestClass { Property1 = t.Property1 });

            //act
            var projection = _projector.ProjectProperties(expression);

            //assert
            projection.ReturnType.Should().Be.EqualTo(typeof(QueryTranslatorTestClass));
            projection.SelectedProperties.Count.Should().Be.EqualTo(1);
            projection.SelectedProperties.Should().Contain(new KeyValuePair<string, string>("Property1", "x"));
            projection.Projection.DynamicInvoke(instance)
                .As<QueryTranslatorTestClass>().Property1.Should().Be.EqualTo("p1");
        }

        [TestMethod]
        public void ProjectProperties_ProjectAnonymousInstance_ReturnsProjectedProperties()
        {
            //prepare
            var instance = new QueryTranslatorTestClass { Property1 = "p1" };
            var expression =
                instance.CreateExpression(t => new { t.Property1, t.Property2, t.Property3, t.Property4, t.Property5 });

            //act
            var projection = _projector.ProjectProperties(expression);

            //assert
            projection.ReturnType.IsAnonymous().Should().Be.True();
            projection.SelectedProperties.Count.Should().Be.EqualTo(5);
            projection.Projection.DynamicInvoke(instance).PropertyValue<string>("Property1").Should().Be.EqualTo("p1");
        }

        [TestMethod]
        public void ProjectProperties_ProjectSingleProperty_ReturnsProjectedProperties()
        {
            //prepare
            var instance = new QueryTranslatorTestClass { Property2 = "p2" };
            var expression = instance.CreateExpression(t => t.Property2);

            //act
            var projection = _projector.ProjectProperties(expression);

            //assert
            projection.ReturnType.Should().Be.EqualTo(typeof(string));
            projection.SelectedProperties.Count.Should().Be.EqualTo(1);
            projection.SelectedProperties.Should().Contain(new KeyValuePair<string, string>("Property2", "y"));
            projection.Projection.DynamicInvoke(instance).Should().Be.EqualTo("p2");
        }
    }
}