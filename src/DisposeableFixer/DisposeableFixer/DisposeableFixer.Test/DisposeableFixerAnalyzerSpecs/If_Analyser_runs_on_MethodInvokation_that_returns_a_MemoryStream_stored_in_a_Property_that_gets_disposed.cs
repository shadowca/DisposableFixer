using FluentAssertions;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace DisposableFixer.Test.DisposeableFixerAnalyzerSpecs
{
    [TestFixture]
    internal class
        If_Analyser_runs_on_MethodInvokation_that_returns_a_MemoryStream_stored_in_a_Property_that_gets_disposed :
            DisposeableFixerAnalyzerSpec
    {
        private Diagnostic[] _diagnostics;

        protected override void BecauseOf()
        {
            _diagnostics = MyHelper.RunAnalyser(Code, Sut);
        }

        private const string Code = @"
namespace DisFixerTest
{
    public class ClassThatUsesFactoyInCtor : IDisposable
    {
        public ClassThatUsesFactoyInCtor()
        {
            var factory = new Factory();

            Property = factory.Create();
        }

        public IDisposable Property { get; }

        public void Dispose()
        {
            Property.Dispose();
        }
    }

    internal class Factory
    {
        public MemoryStream Create()
        {
            return new MemoryStream();
        }
    }
}
";

        [Test]
        public void Then_there_should_be_no_Diagnostics()
        {
            _diagnostics.Length.Should().Be(0);
        }
    }
}