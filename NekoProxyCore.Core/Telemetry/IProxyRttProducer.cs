using System.Threading;
using System.Threading.Tasks;

namespace NekoProxyCore.Core;

public interface IProxyRttProducer
{
    Task<int?> GetRttAsync(CancellationToken cancellationToken = default);
}
