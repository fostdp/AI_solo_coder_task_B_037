using System.Threading.Channels;
using PaEfficiencyTracker.Module.Models;

namespace PaEfficiencyTracker.Module.Channels;

public interface IDataChannels
{
    ChannelReader<PaEfficiencyRequest> PaEfficiencyRequestReader { get; }
    ChannelWriter<PaEfficiencyRequest> PaEfficiencyRequestWriter { get; }
}
