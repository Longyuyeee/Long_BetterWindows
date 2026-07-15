using System.IO;
using System.Text;
using System.Text.Json;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using Serilog;

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

        /// <summary>
        /// 保存数据到文件，使用原子写入防止数据损坏
        /// </summary>
        private void Save()
        {
            // ✅ 在锁外进行 JSON 序列化，减少锁持有时间
            string json;
            _lock.EnterReadLock();
            try
            {
                json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            }
            finally
            {
                _lock.ExitReadLock();
            }

            // ✅ 使用原子写入：先写临时文件，再替换
            var tempFile = _filePath + ".tmp";
            try
            {
                File.WriteAllText(tempFile, json, Encoding.UTF8);
                File.Move(tempFile, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "保存存储数据失败: {FilePath}", _filePath);
                // 清理临时文件
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
                throw;
            }
        }

        public void Dispose()
        {
            _lock.Dispose();
        }
    }
}
