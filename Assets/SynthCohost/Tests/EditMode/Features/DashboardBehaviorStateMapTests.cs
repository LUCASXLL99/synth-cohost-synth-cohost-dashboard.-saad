using NUnit.Framework;
using SynthCohost.Protocol;
using SynthCohost.Runtime.Features.Avatar;

namespace SynthCohost.Tests.EditMode.Features
{
    public sealed class DashboardBehaviorStateMapTests
    {
        [TestCase(AvatarBehavior.Idle, DashboardBehaviorStateMap.Idle)]
        [TestCase(AvatarBehavior.Listening, DashboardBehaviorStateMap.Listening)]
        [TestCase(AvatarBehavior.Thinking, DashboardBehaviorStateMap.Thinking)]
        [TestCase(AvatarBehavior.Speaking, DashboardBehaviorStateMap.Speaking)]
        [TestCase(AvatarBehavior.Happy, DashboardBehaviorStateMap.Happy)]
        [TestCase(AvatarBehavior.Celebrate, DashboardBehaviorStateMap.Celebrate)]
        public void KnownBehaviors_MapToAvatarVerifyStates(AvatarBehavior behavior, string expected)
        {
            Assert.That(DashboardBehaviorStateMap.TryGetStateName(behavior, out var name), Is.True);
            Assert.That(name, Is.EqualTo(expected));
        }

        [Test]
        public void UnknownBehavior_DoesNotMap()
        {
            Assert.That(DashboardBehaviorStateMap.TryGetStateName((AvatarBehavior)999, out var name), Is.False);
            Assert.That(name, Is.Null);
        }

        [Test]
        public void BurstAndLoopFlags_MatchDashboardPolicy()
        {
            Assert.That(DashboardBehaviorStateMap.IsBurst(AvatarBehavior.Happy), Is.True);
            Assert.That(DashboardBehaviorStateMap.IsBurst(AvatarBehavior.Celebrate), Is.True);
            Assert.That(DashboardBehaviorStateMap.IsBurst(AvatarBehavior.Idle), Is.False);
            Assert.That(DashboardBehaviorStateMap.IsLooping(AvatarBehavior.Idle), Is.True);
            Assert.That(DashboardBehaviorStateMap.IsLooping(AvatarBehavior.Listening), Is.True);
            Assert.That(DashboardBehaviorStateMap.IsLooping(AvatarBehavior.Speaking), Is.True);
            Assert.That(DashboardBehaviorStateMap.Speaking, Is.EqualTo("34_Curious_Lean"));
            Assert.That(DashboardBehaviorStateMap.Speaking, Is.Not.EqualTo(DashboardBehaviorStateMap.Listening));
            Assert.That(DashboardBehaviorStateMap.IsLooping(AvatarBehavior.Happy), Is.False);
        }
    }
}
