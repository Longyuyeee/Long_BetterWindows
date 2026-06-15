using System.IO;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class FileOpsService : IFileOpsService
    {
        public Task<HostApiResponse> CopyAsync(string source, string dest)
        {
            return Task.Run(() =>
            {
                try { File.Copy(source, dest, overwrite: false); return HostApiResponse.Success(); }
                catch (Exception ex) { return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse> MoveAsync(string source, string dest)
        {
            return Task.Run(() =>
            {
                try { File.Move(source, dest); return HostApiResponse.Success(); }
                catch (Exception ex) { return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse> DeleteAsync(string path)
        {
            return Task.Run(() =>
            {
                try { if (File.Exists(path)) File.Delete(path); return HostApiResponse.Success(); }
                catch (Exception ex) { return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse<bool>> ExistsAsync(string path)
        {
            return Task.FromResult(HostApiResponse<bool>.Success(File.Exists(path)));
        }
    }
}
