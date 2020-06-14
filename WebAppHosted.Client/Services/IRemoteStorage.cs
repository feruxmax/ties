using System.Threading.Tasks;

namespace WebAppHosted.Client.Services
{
    public interface IRemoteStorage
    {
        Task<VersionedData> GetRemoteData();
        Task Upload(VersionedData data);
    }
}