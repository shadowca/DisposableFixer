using System.Linq;
using DisposableFixer.Extensions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace DisposableFixer.Test.DisposeableFixerAnalyzerSpecs.ObjectCreations
{
    [TestFixture]
    internal class If_Analyser_runs_ObjectCreation_that_is_part_of_auto_property_that_is_disposed : DisposeableFixerAnalyzerSpec
    {
        private const string Code = @"
namespace DisFixerTest.ObjectCreationAssignedToField {
    public class SomeCode : System.IDisposable {
        public System.IDisposable Property { get; } = new System.IO.MemoryStream();

        public void Dispose()
        {
            Property.Dispose();
        }
    }
}";

        private Diagnostic[] _diagnostics;

        protected override void BecauseOf()
        {
            _diagnostics = MyHelper.RunAnalyser(Code, Sut);
        }

        [Test]
        public void Then_there_should_be_no_Diagnostics()
        {
            var diagnostic = _diagnostics.First();
            _diagnostics.Should().BeEmpty();
        }
    }
}