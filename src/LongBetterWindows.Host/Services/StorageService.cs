using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class StorageService : IStorageService
    {
        private readonly string _filePath;
        private readonly ReaderWriterLockSlim _lock = new();
        private Dictionary<string, string> _data;

        public StorageService(string? filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                AppContext.BaseDirectory, "config", "storage.json");

            var dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _data = Load();
        }

        public Task<HostApiResponse<string?>> GetAsync(string key)
        {
            return Task.Run(() =>
            {
                _lock.EnterReadLock();
                try
                {
                    _data.TryGetValue(key, out var value);
                    return HostApiResponse<string?>.Success(value);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<string?>.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            });
        }

        public Task<HostApiResponse> SetAsync(string key, string value)
        {
            return Task.Run(() =>
            {
                _lock.EnterWriteLock();
                try
                {
                    _data[key] = value;
                    Save();
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            });
        }

        public Task<HostApiResponse> DeleteAsync(string key)
        {
            return Task.Run(() =>
            {
                _lock.EnterWriteLock();
                try
                {
                    _data.Remove(key);
                    Save();
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            });
        }

        public Task<HostApiResponse<bool>> ContainsKeyAsync(string key)
        {
            return Task.Run(() =>
            {
                _lock.EnterReadLock();
                try
                {
                    return HostApiResponse<bool>.Success(_data.ContainsKey(key));
                }
                catch (Exception ex)
                {
                    return HostApiResponse<bool>.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            });
        }

        private Dictionary<string, string> Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                        ?? new Dictionary<string, string>();
                }
            }
            catch
            {
                // 文件损坏时从空数据开始
            }

            return new Dictionary<string, string>();
        }

        private void Save()
        {
            var json = JsonSerializer.Serialize(_data,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }

        public void Dispose()
        {
            _lock.Dispose();
        }
    }
}
