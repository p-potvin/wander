using Wander.Core.Services;
using Xunit;

namespace Wander.Tests
{
    public class SyncControllerTests
    {
        [Fact]
        public void StartsRunning()
        {
            Assert.False(new SyncController().IsPaused);
        }

        [Fact]
        public void ToggleFlipsAndReturnsNewState()
        {
            var c = new SyncController();
            Assert.True(c.Toggle());
            Assert.True(c.IsPaused);
            Assert.False(c.Toggle());
            Assert.False(c.IsPaused);
        }

        [Fact]
        public void RaisesEventOnlyOnActualChange()
        {
            var c = new SyncController();
            var changes = 0;
            var last = false;
            c.PausedChanged += (_, paused) => { changes++; last = paused; };

            c.Pause();
            c.Pause();   // no-op, already paused
            c.Resume();
            c.Resume();  // no-op, already running

            Assert.Equal(2, changes);
            Assert.False(last);
        }
    }
}
