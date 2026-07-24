using System;
using System.Diagnostics;
using Avalonia.Rendering;
using Avalonia.Threading;

namespace Avalonia.SilkNet
{
    /// <summary>
    /// Runs the Avalonia render loop on the Silk.NET UI dispatcher.
    /// </summary>
    internal sealed class SilkNetRenderTimer : DefaultRenderTimer
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public SilkNetRenderTimer(int framesPerSecond)
            : base(framesPerSecond)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);
            Interval = TimeSpan.FromSeconds(1.0 / framesPerSecond);
        }

        internal TimeSpan Interval { get; }

        public override bool RunsInBackground => false;

        protected override IDisposable StartCore(Action<TimeSpan> tick)
        {
            return DispatcherTimer.Run(
                () =>
                {
                    tick(_clock.Elapsed);
                    return true;
                },
                Interval,
                DispatcherPriority.Render);
        }
    }
}
