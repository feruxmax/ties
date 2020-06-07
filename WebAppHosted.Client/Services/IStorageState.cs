namespace WebAppHosted.Client.Services
{
    public interface IStorageState
    {
        bool Synced { get; set; }
    }
}