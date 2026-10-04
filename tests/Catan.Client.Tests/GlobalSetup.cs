using Catan.Client.Animation;
using NUnit.Framework;

namespace Catan.Client.Tests
{
    /// <summary>
    /// Animations are off by default in tests so screenshots and clicks see the finished state at once.
    /// <see cref="AnimationTests"/> turns them on with a hand-cranked clock.
    /// </summary>
    [SetUpFixture]
    public class GlobalSetup
    {
        [OneTimeSetUp]
        public void DisableAnimations() => AnimationLayer.Enabled = false;
    }
}
