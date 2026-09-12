using System;
using OpenUtau.Core.Render;
using Xunit;

namespace OpenUtau.Core {
    public class WorldlineCurveTest {
        [Fact]
        public void TailFramesHoldLastControlValueWithoutMutatingInput() {
            var curve = new double[] { 0.4, 0.7 };
            Assert.Equal(new double[] { 0.4, 0.7, 0.7, 0.7 },
                Worldline.FitSynthesisCurve(curve, 4, 0.5));
            Assert.Equal(new double[] { 0.4, 0.7 }, curve);
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(1.0)]
        public void MissingControlsUseNeutralValue(double neutral) {
            Assert.Equal(new double[] { neutral, neutral }, Worldline.FitSynthesisCurve(null, 2, neutral));
            Assert.Equal(new double[] { neutral, neutral }, Worldline.FitSynthesisCurve(Array.Empty<double>(), 2, neutral));
        }

        [Fact]
        public void CompleteCurvesKeepTheirSamples() {
            var curve = new double[] { 0.1, 0.8, 0.3 };
            Assert.Same(curve, Worldline.FitSynthesisCurve(curve, 3, 0.5));
            Assert.Same(curve, Worldline.FitSynthesisCurve(curve, 2, 0.5));
        }
    }
}
