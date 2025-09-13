using System.Threading.Tasks;

namespace HttpCheckService
{
    public interface IManageableApplication
    {
        string Name { get; }
        Task<bool> IsRunningAsync();
        void Start();
        void Stop();
    }
} 