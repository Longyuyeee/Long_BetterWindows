#include <windows.h>
#include <shellapi.h>
#include <shobjidl_core.h>

#include <atomic>
#include <new>
#include <string>

namespace
{
    // Must match appxmanifest.xml com:Class and desktop5:Verb registrations.
    constexpr CLSID CLSID_LongFolderNoteCommand =
    { 0xa17f41ad, 0x74bc, 0x47f8, { 0x98, 0x4b, 0x2d, 0xf6, 0xf2, 0x22, 0x63, 0xa1 } };

    HMODULE g_module = nullptr;
    std::atomic_ulong g_objectCount = 0;

    HRESULT DuplicateString(const wchar_t* value, PWSTR* destination) noexcept
    {
        if (destination == nullptr)
        {
            return E_POINTER;
        }

        *destination = nullptr;
        const size_t length = wcslen(value) + 1;
        const size_t bytes = length * sizeof(wchar_t);
        auto* copy = static_cast<PWSTR>(CoTaskMemAlloc(bytes));
        if (copy == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        memcpy(copy, value, bytes);
        *destination = copy;
        return S_OK;
    }

    HRESULT GetFolderPath(IShellItemArray* selection, std::wstring& path) noexcept
    {
        if (selection == nullptr)
        {
            return E_INVALIDARG;
        }

        DWORD count = 0;
        HRESULT result = selection->GetCount(&count);
        if (FAILED(result) || count != 1)
        {
            return FAILED(result) ? result : E_INVALIDARG;
        }

        IShellItem* item = nullptr;
        result = selection->GetItemAt(0, &item);
        if (FAILED(result))
        {
            return result;
        }

        SFGAOF attributes = 0;
        result = item->GetAttributes(SFGAO_FOLDER | SFGAO_FILESYSTEM, &attributes);
        if (SUCCEEDED(result)
            && (attributes & (SFGAO_FOLDER | SFGAO_FILESYSTEM))
                != (SFGAO_FOLDER | SFGAO_FILESYSTEM))
        {
            result = E_INVALIDARG;
        }

        PWSTR rawPath = nullptr;
        if (SUCCEEDED(result))
        {
            result = item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);
        }
        item->Release();

        if (FAILED(result))
        {
            return result;
        }

        try
        {
            path.assign(rawPath);
        }
        catch (...)
        {
            CoTaskMemFree(rawPath);
            return E_OUTOFMEMORY;
        }

        CoTaskMemFree(rawPath);
        return path.empty() ? E_INVALIDARG : S_OK;
    }

    HRESULT GetHostPath(std::wstring& hostPath) noexcept
    {
        wchar_t modulePath[32768]{};
        const DWORD length = GetModuleFileNameW(g_module, modulePath, ARRAYSIZE(modulePath));
        if (length == 0 || length >= ARRAYSIZE(modulePath))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        try
        {
            std::wstring directory(modulePath, length);
            size_t separator = directory.find_last_of(L"\\/");
            if (separator == std::wstring::npos)
            {
                return E_UNEXPECTED;
            }
            directory.resize(separator);
            separator = directory.find_last_of(L"\\/");
            if (separator == std::wstring::npos)
            {
                return E_UNEXPECTED;
            }
            directory.resize(separator + 1);
            hostPath = directory + L"LongBetterWindows.Host.exe";
        }
        catch (...)
        {
            return E_OUTOFMEMORY;
        }

        const DWORD attributes = GetFileAttributesW(hostPath.c_str());
        return attributes == INVALID_FILE_ATTRIBUTES
            || (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0
                ? HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND)
                : S_OK;
    }

    std::wstring QuoteCommandLineArgument(const std::wstring& value)
    {
        std::wstring quoted;
        quoted.reserve(value.size() + 2);
        quoted.push_back(L'"');

        size_t backslashes = 0;
        for (const wchar_t character : value)
        {
            if (character == L'\\')
            {
                ++backslashes;
                continue;
            }

            if (character == L'"')
            {
                quoted.append(backslashes * 2 + 1, L'\\');
                quoted.push_back(L'"');
            }
            else
            {
                quoted.append(backslashes, L'\\');
                quoted.push_back(character);
            }
            backslashes = 0;
        }

        quoted.append(backslashes * 2, L'\\');
        quoted.push_back(L'"');
        return quoted;
    }

    class FolderNoteCommand final : public IExplorerCommand
    {
    public:
        FolderNoteCommand() noexcept
        {
            ++g_objectCount;
        }

        IFACEMETHODIMP QueryInterface(REFIID interfaceId, void** value) noexcept override
        {
            if (value == nullptr)
            {
                return E_POINTER;
            }
            *value = nullptr;
            if (interfaceId == IID_IUnknown || interfaceId == IID_IExplorerCommand)
            {
                *value = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() noexcept override
        {
            return ++referenceCount_;
        }

        IFACEMETHODIMP_(ULONG) Release() noexcept override
        {
            const ULONG remaining = --referenceCount_;
            if (remaining == 0)
            {
                delete this;
            }
            return remaining;
        }

        IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) noexcept override
        {
            return DuplicateString(L"备注此文件夹", title);
        }

        IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) noexcept override
        {
            if (icon == nullptr)
            {
                return E_POINTER;
            }
            *icon = nullptr;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* toolTip) noexcept override
        {
            return DuplicateString(L"使用 Long 为此文件夹添加本地备注", toolTip);
        }

        IFACEMETHODIMP GetCanonicalName(GUID* commandName) noexcept override
        {
            if (commandName == nullptr)
            {
                return E_POINTER;
            }
            *commandName = CLSID_LongFolderNoteCommand;
            return S_OK;
        }

        IFACEMETHODIMP GetState(
            IShellItemArray* selection,
            BOOL,
            EXPCMDSTATE* state) noexcept override
        {
            if (state == nullptr)
            {
                return E_POINTER;
            }

            std::wstring folderPath;
            *state = SUCCEEDED(GetFolderPath(selection, folderPath))
                ? ECS_ENABLED
                : ECS_HIDDEN;
            return S_OK;
        }

        IFACEMETHODIMP Invoke(
            IShellItemArray* selection,
            IBindCtx*) noexcept override
        {
            std::wstring folderPath;
            HRESULT result = GetFolderPath(selection, folderPath);
            if (FAILED(result))
            {
                return result;
            }

            std::wstring hostPath;
            result = GetHostPath(hostPath);
            if (FAILED(result))
            {
                return result;
            }

            std::wstring arguments;
            try
            {
                arguments = L"--note " + QuoteCommandLineArgument(folderPath);
            }
            catch (...)
            {
                return E_OUTOFMEMORY;
            }

            SHELLEXECUTEINFOW executeInfo{
                .cbSize = sizeof(SHELLEXECUTEINFOW),
                .fMask = SEE_MASK_FLAG_NO_UI | SEE_MASK_NOASYNC,
                .lpFile = hostPath.c_str(),
                .lpParameters = arguments.c_str(),
                .nShow = SW_SHOWNORMAL,
            };
            return ShellExecuteExW(&executeInfo)
                ? S_OK
                : HRESULT_FROM_WIN32(GetLastError());
        }

        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) noexcept override
        {
            if (flags == nullptr)
            {
                return E_POINTER;
            }
            *flags = ECF_DEFAULT;
            return S_OK;
        }

        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) noexcept override
        {
            if (commands == nullptr)
            {
                return E_POINTER;
            }
            *commands = nullptr;
            return E_NOTIMPL;
        }

    private:
        ~FolderNoteCommand()
        {
            --g_objectCount;
        }

        std::atomic_ulong referenceCount_{ 1 };
    };

    class CommandClassFactory final : public IClassFactory
    {
    public:
        CommandClassFactory() noexcept
        {
            ++g_objectCount;
        }

        IFACEMETHODIMP QueryInterface(REFIID interfaceId, void** value) noexcept override
        {
            if (value == nullptr)
            {
                return E_POINTER;
            }
            *value = nullptr;
            if (interfaceId == IID_IUnknown || interfaceId == IID_IClassFactory)
            {
                *value = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() noexcept override
        {
            return ++referenceCount_;
        }

        IFACEMETHODIMP_(ULONG) Release() noexcept override
        {
            const ULONG remaining = --referenceCount_;
            if (remaining == 0)
            {
                delete this;
            }
            return remaining;
        }

        IFACEMETHODIMP CreateInstance(
            IUnknown* outer,
            REFIID interfaceId,
            void** value) noexcept override
        {
            if (outer != nullptr)
            {
                return CLASS_E_NOAGGREGATION;
            }
            if (value == nullptr)
            {
                return E_POINTER;
            }

            auto* command = new (std::nothrow) FolderNoteCommand();
            if (command == nullptr)
            {
                *value = nullptr;
                return E_OUTOFMEMORY;
            }
            const HRESULT result = command->QueryInterface(interfaceId, value);
            command->Release();
            return result;
        }

        IFACEMETHODIMP LockServer(BOOL lock) noexcept override
        {
            if (lock)
            {
                ++g_objectCount;
            }
            else
            {
                --g_objectCount;
            }
            return S_OK;
        }

    private:
        ~CommandClassFactory()
        {
            --g_objectCount;
        }

        std::atomic_ulong referenceCount_{ 1 };
    };
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return g_objectCount == 0 ? S_OK : S_FALSE;
}

extern "C" HRESULT __stdcall DllGetClassObject(
    REFCLSID classId,
    REFIID interfaceId,
    void** value)
{
    if (classId != CLSID_LongFolderNoteCommand)
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }
    if (value == nullptr)
    {
        return E_POINTER;
    }

    auto* factory = new (std::nothrow) CommandClassFactory();
    if (factory == nullptr)
    {
        *value = nullptr;
        return E_OUTOFMEMORY;
    }
    const HRESULT result = factory->QueryInterface(interfaceId, value);
    factory->Release();
    return result;
}
