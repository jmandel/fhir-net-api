﻿/* 
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.githubusercontent.com/FirelyTeam/fhir-net-api/master/LICENSE
 */

// To introduce the DSTU2 FHIR specification
// extern alias dstu2;

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Model.Primitives;
using Hl7.FhirPath.Functions;
using Hl7.FhirPath.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks.Dataflow;

namespace Hl7.FhirPath.R4.Tests
{
    [TestClass]
    public class FhirPathTest
    {
        /// <summary>
        ///  Gets or sets the test context which provides
        ///  information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void TestFhirPathPolymporphism()
        {
            var patient = new Patient() { Active = false };
            patient.Meta = new Meta() { LastUpdated = new DateTimeOffset(2018, 5, 24, 14, 48, 0, TimeSpan.Zero) };
            var nav = patient.ToTypedElement();

            var result = nav.Select("Resource.meta.lastUpdated");
            Assert.IsNotNull(result.FirstOrDefault());
            Assert.AreEqual(PartialDateTime.Parse("2018-05-24T14:48:00+00:00"), result.First().Value);
        }

        [TestMethod]
        public void TestFhirPathTrace()
        {
            var patient = new Hl7.Fhir.Model.Patient() { Id = "pat45", Active = false };
            patient.Meta = new Meta() { LastUpdated = new DateTimeOffset(2018, 5, 24, 14, 48, 0, TimeSpan.Zero) };
            var nav = patient.ToTypedElement();

            EvaluationContext ctx = new FhirEvaluationContext();
            var result = nav.Select("Resource.meta.trace('log').lastUpdated", ctx);
            Assert.IsNotNull(result.FirstOrDefault());
            Assert.AreEqual(PartialDateTime.Parse("2018-05-24T14:48:00+00:00"), result.First().Value);

            bool traced = false;
            ctx.Tracer = (string name, System.Collections.Generic.IEnumerable<ITypedElement> results) =>
            {
                System.Diagnostics.Trace.WriteLine($"{name}");
                Assert.AreEqual("log", name);
                foreach (ITypedElement item in results)
                {
                    var fhirValue = item.Annotation<IFhirValueProvider>();
                    System.Diagnostics.Trace.WriteLine($"--({fhirValue.FhirValue.GetType().Name}): {item.Value} {fhirValue.FhirValue}");
                    Assert.AreEqual(patient.Meta, fhirValue.FhirValue);
                    traced = true;
                }
            };
            result = nav.Select("Resource.meta.trace('log').lastUpdated", ctx);
            Assert.IsNotNull(result.FirstOrDefault());
            Assert.AreEqual(PartialDateTime.Parse("2018-05-24T14:48:00+00:00"), result.First().Value);
            Assert.IsTrue(traced);

            traced = false;
            ctx.Tracer = (string name, System.Collections.Generic.IEnumerable<ITypedElement> results) =>
            {
                System.Diagnostics.Trace.WriteLine($"{name}");
                Assert.IsTrue(name == "id" || name == "log");
                foreach (ITypedElement item in results)
                {
                    var fhirValue = item.Annotation<IFhirValueProvider>();
                    System.Diagnostics.Trace.WriteLine($"--({fhirValue.FhirValue.GetType().Name}): {item.Value} {fhirValue.FhirValue}");
                    traced = true;
                }
            };
            result = nav.Select("Resource.trace('id', id).meta.trace('log', lastUpdated).lastUpdated", ctx);
            Assert.IsNotNull(result.FirstOrDefault());
            Assert.AreEqual(PartialDateTime.Parse("2018-05-24T14:48:00+00:00"), result.First().Value);
            Assert.IsTrue(traced);
        }

        [TestMethod]
        public void TestFhirPathCombine2()
        {
            var cs = new Hl7.Fhir.Model.CodeSystem() { Id = "pat45" };
            cs.Concept.Add(new CodeSystem.ConceptDefinitionComponent()
            {
                Code = "5",
                Display = "Five"
            });
            var nav = cs.ToTypedElement();

            EvaluationContext ctx = new EvaluationContext();
            var result = nav.Predicate("concept.code.combine($this.descendants().concept.code).isDistinct()", ctx);
            Assert.IsTrue(result);

            cs.Concept.Add(new CodeSystem.ConceptDefinitionComponent()
            {
                Code = "5",
                Display = "Five"
            });
            result = nav.Predicate("concept.code.combine($this.descendants().concept.code).isDistinct()", ctx);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TestFhirPathCombine()
        {
            var patient = new Patient();

            patient.Identifier.Add(new Identifier("http://nu.nl", "id1"));
            patient.Identifier.Add(new Identifier("http://nu.nl", "id2"));

            var result = patient.Predicate("identifier[0].value.combine($this.identifier[1].value).isDistinct()");
            Assert.IsTrue(result);

            result = patient.Predicate("identifier[0].value.combine($this.identifier[0].value).isDistinct()");
            Assert.IsFalse(result);
        }


        [TestMethod]
        public void TestImplicitQuantityCast()
        {
            var obs = new Observation { Value = new Hl7.Fhir.Model.Quantity(75m, "kg") };
            Assert.IsTrue(obs.ToTypedElement().Predicate("Observation.value > 74 'kg'"));
            Assert.IsTrue(obs.ToTypedElement().Predicate("Observation.value = 75 'kg'"));
            Assert.IsTrue(obs.ToTypedElement().Predicate("Observation.value ~ 75 'kg'"));
        }

        [TestMethod]
        [TestCategory("LongRunner")]
        public void TestFhirPathScalarInParallel()
        {
            var patient = new Patient();

            patient.Identifier.Add(new Identifier("http://nu.nl", "id1"));

            var element = patient.ToTypedElement();

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var actionBlock = new ActionBlock<string>(
                 (input =>
                 {
                     element.Select(input);
                 }),
                 new ExecutionDataflowBlockOptions()
                 {
                     MaxDegreeOfParallelism = 20
                 });


            for (int i = 0; i < 20_000; i++)
            {
                actionBlock.Post($"identifier[{i}]");
            }
            actionBlock.Complete();
            actionBlock.Completion.Wait();

            stopwatch.Stop();

            TestContext.WriteLine($"Select in 20.000 : {stopwatch.ElapsedMilliseconds}ms");
        }

        //[TestMethod]
        //public void TypeInfoAndNativeMatching()
        //{
        //    Assert.True(TypeInfo.Decimal.MapsToNative(typeof(decimal)));
        //    Assert.False(TypeInfo.Decimal.MapsToNative(typeof(long)));
        //    Assert.False(TypeInfo.Any.CanBeCastTo(typeof(long)));
        //}

    }
}