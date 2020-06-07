using System.Threading.Tasks;

namespace WebAppHosted.Client.Services
{
    public interface IStorage
    {
        Task<T> GetItemAsync<T>(string key);
        Task SetItemAsync<T>(string key, T data);
    }
}