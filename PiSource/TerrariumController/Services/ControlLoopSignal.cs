using System.Threading.Channels;

namespace TerrariumController.Services
{
    public interface IControlLoopSignal
    {
        void RequestImmediateEvaluation(string reason);
        Task<string> WaitForSignalAsync(CancellationToken cancellationToken);
    }

    public class ControlLoopSignal : IControlLoopSignal
    {
        private readonly Channel<string> _signals = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        public void RequestImmediateEvaluation(string reason)
        {
            _signals.Writer.TryWrite(reason);
        }

        public async Task<string> WaitForSignalAsync(CancellationToken cancellationToken)
        {
            return await _signals.Reader.ReadAsync(cancellationToken);
        }
    }
}