using System.Threading.Tasks;

namespace WebAppHosted.Client.Services
{
    public interface ISyncService
    {
        Task Sync();
    }
}