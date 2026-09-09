using System;
using NUnit.Framework;
using SynthCohost.Runtime.Development;

namespace SynthCohost.Tests.EditMode.Development
{
    public sealed class LiveTestPanelLayoutTests
    {
        [Test]
        public void CompactPreferredWidth_FitsInsideTypicalGameView()
        {
            var rect = LiveTestPanelLayout.CalculatePanelRect(
                LiveTestPanelLayout.CompactPreferredWidth,
                1280f,
                720f);

            Assert.That(rect.width, Is.EqualTo(LiveTestPanelLayout.CompactPreferredWidth));
            Assert.That(rect.xMax, Is.LessThanOrEqualTo(1280f - 16f));
        }

        [Test]
        public void ShowButtonRect_StaysInTheTopLeftMargin()
        {
            var rect = LiveTestPanelLayout.CalculateShowButtonRect();

            Assert.That(rect.xMin, Is.EqualTo(LiveTestPanelLayout.ScreenMargin));
            Assert.That(rect.yMin, Is.EqualTo(LiveTestPanelLayout.ScreenMargin));
            Assert.That(rect.width, Is.EqualTo(LiveTestPanelLayout.ShowButtonWidth));
        }

        [Test]
        public void PreferredPanel_FitsInsideAvailableGameView()
        {
            var rect = LiveTestPanelLayout.CalculatePanelRect(680f, 728f, 900f);

            Assert.That(rect.xMin, Is.EqualTo(16f));
            Assert.That(rect.yMin, Is.EqualTo(16f));
            Assert.That(rect.xMax, Is.LessThanOrEqualTo(728f - 16f));
            Assert.That(rect.yMax, Is.LessThanOrEqualTo(900f - 16f));
        }

        [Test]
        public void NarrowGameView_NeverForcesTheOld420PixelOverflow()
        {
            var rect = LiveTestPanelLayout.CalculatePanelRect(680f, 300f, 200f);

            Assert.That(rect.width, Is.EqualTo(268f));
            Assert.That(rect.height, Is.EqualTo(168f));
            Assert.That(rect.xMax, Is.EqualTo(284f));
            Assert.That(rect.yMax, Is.EqualTo(184f));
        }

        [Test]
        public void ButtonWidthsStayWithinConstrainedContentWidth()
        {
            const float contentWidth = 640f;
            var controlWidth = LiveTestPanelLayout.CalculateControlWidth(contentWidth);

            var threeButtonWidth = LiveTestPanelLayout.CalculateButtonWidth(controlWidth, 3);
            var occupied = threeButtonWidth * 3f + LiveTestPanelLayout.ButtonGap * 2f;

            Assert.That(occupied, Is.EqualTo(controlWidth).Within(0.001f));
            Assert.That(
                occupied + LiveTestPanelLayout.ControlHorizontalReserve,
                Is.LessThanOrEqualTo(contentWidth));
            Assert.That(LiveTestPanelLayout.ShouldStackButtons(controlWidth), Is.False);
            Assert.That(LiveTestPanelLayout.ShouldStackButtons(320f), Is.True);
        }

        [Test]
        public void EndpointDisplay_OmitsUserInfoQueryAndFragment()
        {
            var endpoint = new Uri("wss://user:secret@example.com:8443/ws?token=private#fragment");

            var display = SynthCohostLiveTestPanel.FormatEndpointForDisplay(endpoint);

            Assert.That(display, Is.EqualTo("wss://example.com:8443/ws"));
            Assert.That(display, Does.Not.Contain("user"));
            Assert.That(display, Does.Not.Contain("secret"));
            Assert.That(display, Does.Not.Contain("private"));
        }

        [Test]
        public void ErrorDisplaySanitizer_RemovesControlCharactersAndBoundsLength()
        {
            var sanitized = SynthCohostLiveTestPanel.SanitizeForDisplay(
                "expired\nforged-log-entry\twith-details",
                16);

            Assert.That(sanitized, Does.Not.Contain("\n"));
            Assert.That(sanitized, Does.Not.Contain("\t"));
            Assert.That(sanitized, Has.Length.LessThanOrEqualTo(19));
            Assert.That(sanitized, Does.EndWith("..."));
        }
    }
}
