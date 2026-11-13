// 界面份数限制：一台机器上同时最多开四份界面，第五份不开。
//
// 判定方式：系统临时目录里每人放一份记录文件，名字带自己的进程号
// （texttool-gui-<进程号>.lock），启动时先把进程已经不在了的记录删掉，再数还剩几份。
// 记录不写在程序目录里，也不要管理员权限；进程被强杀留下的记录，下一次启动会被清掉。
//
// 探活：Linux 用 kill(pid, 0)，Windows 用 OpenProcess 加 GetExitCodeProcess。
// 两者的判据都是「这个进程号还在不在」，不核对对方跑的是哪个可执行文件：进程号被别的程序
// 回收以后，那一份旧记录会一直算作开着的，要等那个进程结束，或者手工删掉临时目录里那份记录。
//
// 先落自己那一份再数，是为了压住两个人同时启动时的抢位：谁先数到超过四份谁就退出去，
// 最坏的情况是四份里还差一份时两个人同时启动、两个都不开（保守，不会多开）。
//
// 这个头文件只给界面入口一处用，所以整份都写在头里，函数都是 inline。
#pragma once

#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <string>
#include <system_error>

#ifdef _WIN32
#include <windows.h>
#else
#include <cerrno>
#include <csignal>
#include <sys/types.h>
#include <unistd.h>
#endif

namespace texttool::ui {

// 同时最多开这么多份界面。数的是「记录还在」的份数，自己也算一份。
constexpr int kMaxRunningInstances = 4;

// 记录文件名的固定两头：中间是进程号，两头一起用来认「这是本程序的记录」。
constexpr const char* kRecordPrefix = "texttool-gui-";
constexpr const char* kRecordSuffix = ".lock";

// 自己这一次开起来以后占着的那份记录。claim_running_instance 填，release_running_instance 删。
struct RunningInstance {
    std::string record_path;  // 记录的完整路径；空表示没登记上，退出时也就没有要删的
};

namespace run_guard_detail {

inline long current_pid() {
#ifdef _WIN32
    return static_cast<long>(GetCurrentProcessId());
#else
    return static_cast<long>(getpid());
#endif
}

// 系统临时目录。找不到就返回空路径，调用方据此放弃这一轮限制（宁可让程序开起来）。
inline std::filesystem::path temporary_dir() {
    std::error_code error;
    std::filesystem::path dir = std::filesystem::temp_directory_path(error);
    if (!error && !dir.empty()) return dir;

    const char* fallback = std::getenv("TMPDIR");
    if (fallback == nullptr || *fallback == '\0') fallback = std::getenv("TEMP");
    if (fallback == nullptr || *fallback == '\0') return {};
    std::filesystem::path from_env(fallback);
    if (!std::filesystem::is_directory(from_env, error)) return {};
    return from_env;
}

// 进程号还活着吗。读不出来、不让读（权限不够）都按「还活着」算：宁可少开一份，
// 也不要把用户挡在门外开场之后再把别人的记录删掉。
inline bool process_alive(long pid) {
    if (pid <= 0) return false;
#ifdef _WIN32
    HANDLE handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, static_cast<DWORD>(pid));
    if (handle == nullptr) {
        // ERROR_INVALID_PARAMETER 说明没有这个进程号；拒绝访问说明进程在，只是不归我们管。
        return GetLastError() != ERROR_INVALID_PARAMETER;
    }
    DWORD code = 0;
    const bool alive = GetExitCodeProcess(handle, &code) == FALSE || code == STILL_ACTIVE;
    CloseHandle(handle);
    return alive;
#else
    if (kill(static_cast<pid_t>(pid), 0) == 0) return true;
    // EPERM：进程在，只是不归我们这个用户管。ESRCH：进程没了。
    return errno == EPERM;
#endif
}

// 文件名里那两个固定头去掉，剩下的全当进程号：空、有别的字符、太长都算认不出来。
inline long pid_from_name(const std::string& name) {
    const std::string prefix(kRecordPrefix);
    const std::string suffix(kRecordSuffix);
    if (name.size() <= prefix.size() + suffix.size()) return 0;
    if (name.compare(0, prefix.size(), prefix) != 0) return 0;
    if (name.compare(name.size() - suffix.size(), suffix.size(), suffix) != 0) return 0;

    const std::string digits = name.substr(prefix.size(), name.size() - prefix.size() - suffix.size());
    if (digits.size() > 9) return 0;
    long pid = 0;
    for (char ch : digits) {
        if (ch < '0' || ch > '9') return 0;
        pid = pid * 10 + (ch - '0');
    }
    return pid;
}

// 把临时目录里那批记录挨个看一遍，进程已经不在的删掉；顺手数还剩几份。
// 名字认不出来的不动：不是我们写的东西，不替别人删。
inline int sweep_and_count(const std::filesystem::path& dir) {
    std::error_code error;
    std::filesystem::directory_iterator entry(dir, error);
    if (error) return 0;

    int count = 0;
    for (const std::filesystem::directory_entry& item : entry) {
        std::error_code entry_error;
        if (!item.is_regular_file(entry_error)) continue;
        const std::string name = item.path().filename().string();
        const long pid = pid_from_name(name);
        if (pid <= 0) continue;

        if (!process_alive(pid)) {
            std::error_code remove_error;
            std::filesystem::remove(item.path(), remove_error);
            continue;
        }
        ++count;
    }
    return count;
}

}  // namespace run_guard_detail

// 删掉自己那一份记录。正常退出时调用一次；被拒绝启动的那一份在 claim_running_instance 里
// 就地删掉，这里不用再管。
// 声明写在这里，是因为 claim_running_instance 数完份数以后要自己收回去。
inline void release_running_instance(RunningInstance* instance);

// 占自己那一份记录，并把现在的份数写进 *running。
//
// 返回 false 表示这一份不该开（已经有四份开着）：这时临时目录里没有自己那一份记录，
// 调用方提示一句就退出，退出码要是非零。
// 返回 true 时调用方负责在退出前调 release_running_instance。
inline bool claim_running_instance(RunningInstance* instance, int* running) {
    if (instance == nullptr) return false;
    instance->record_path.clear();
    if (running != nullptr) *running = 0;

    const std::filesystem::path dir = run_guard_detail::temporary_dir();
    if (dir.empty()) {
        std::fprintf(stderr, "[界面] 找不到临时目录，同时开四份的限制这一轮没生效\n");
        return true;
    }

    // 先把残留清了：上一次被强杀的那几份还留着记录，不清掉会把位置白占着。
    run_guard_detail::sweep_and_count(dir);

    const long pid = run_guard_detail::current_pid();
    const std::filesystem::path record =
        dir / (std::string(kRecordPrefix) + std::to_string(pid) + kRecordSuffix);
    {
        std::ofstream out(record, std::ios::trunc);
        if (!out) {
            // 写不下就不登记：程序照常开，标准错误说一句，免得用户以为限制生效了。
            std::fprintf(stderr, "[界面] 写不了份数记录：%s，这一轮没数\n", record.string().c_str());
            return true;
        }
        out << pid << "\n";
    }
    instance->record_path = record.string();

    // 自己这一份已经落盘，现在数的就是「连我在内一共几份」。
    const int alive = run_guard_detail::sweep_and_count(dir);
    if (running != nullptr) *running = alive;
    if (alive > kMaxRunningInstances) {
        release_running_instance(instance);
        return false;
    }
    return true;
}

// 删掉自己那一份记录。
inline void release_running_instance(RunningInstance* instance) {
    if (instance == nullptr || instance->record_path.empty()) return;
    std::error_code error;
    std::filesystem::remove(instance->record_path, error);
    instance->record_path.clear();
}

}  // namespace texttool::ui
