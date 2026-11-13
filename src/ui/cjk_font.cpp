#include "ui/cjk_font.h"

#include <FL/Fl.H>

#include <cstring>
#include <cwchar>
#include <filesystem>
#include <string>
#include <system_error>

#include "engine/schemes.h"

#ifdef _WIN32
#include <windows.h>
#else
#include <fontconfig/fontconfig.h>
#endif

namespace texttool::ui {

namespace {

// 自带字体的文件名与家族名。文件名与 CMakeLists 里复制到可执行文件旁边的那一份一致，
// 家族名与 assets/README.md 写的一致。
constexpr const char* kBundledFile = "TextToolWenKai-Regular.ttf";
constexpr const char* kBundledFamily = "TextTool WenKai";

// 自带字体找没找到。先按「与程序同一个目录」，再按「程序目录下的 fonts 子目录」。
std::filesystem::path find_bundled_font(const std::string& program_path) {
    const std::filesystem::path dir = texttool::program_dir(program_path.c_str());
    const std::filesystem::path candidates[] = {dir / kBundledFile, dir / "fonts" / kBundledFile};
    for (const std::filesystem::path& candidate : candidates) {
        std::error_code ignored;
        if (std::filesystem::is_regular_file(candidate, ignored)) return candidate;
    }
    return {};
}

#ifdef _WIN32

// 注册之后问一句 GDI：按这个家族名要一款字体，它给回来的是不是这一款。
// 要有这一步，是因为注册返回成功、字体其实没进到能用的状态时，界面上会静默变回方框，
// 只有比对回读到的名字才看得出来。
bool gdi_finds_family(const wchar_t* family) {
    HDC dc = CreateCompatibleDC(nullptr);
    if (dc == nullptr) return false;

    LOGFONTW lf = {};
    lf.lfHeight = -12;
    lf.lfCharSet = DEFAULT_CHARSET;
    lf.lfWeight = FW_NORMAL;
    size_t length = wcslen(family);
    if (length >= LF_FACESIZE) length = LF_FACESIZE - 1;
    memcpy(lf.lfFaceName, family, length * sizeof(wchar_t));
    lf.lfFaceName[length] = L'\0';

    bool found = false;
    HFONT font = CreateFontIndirectW(&lf);
    if (font != nullptr) {
        HGDIOBJ previous = SelectObject(dc, font);
        wchar_t face[LF_FACESIZE] = {};
        if (GetTextFaceW(dc, LF_FACESIZE, face) > 0) found = wcscmp(face, family) == 0;
        SelectObject(dc, previous);
        DeleteObject(font);
    }
    DeleteDC(dc);
    return found;
}

// 把自带字体装进本进程（不落到系统里），装上了再确认家族名能查到。
bool register_bundled_font(const std::filesystem::path& file) {
    const std::wstring path = file.wstring();
    if (AddFontResourceExW(path.c_str(), FR_PRIVATE, nullptr) == 0) return false;
    return gdi_finds_family(L"TextTool WenKai");
}

// 系统自带的几款中文字体，从新到旧试。
// 微软雅黑从 Vista 起就有，是这几款里字形最全的，所以排第一。
const char* const kCandidates[] = {"Microsoft YaHei", "SimHei", "SimSun", nullptr};

std::string pick_family() {
    for (const char* const* name = kCandidates; *name != nullptr; ++name) {
        // FLTK 的 set_font 按名字建字体，名字不存在时会退回到内置的英文字体，
        // 没有 API 能回问它到底用上了哪一款，所以这里只按顺序给候选。
        return *name;
    }
    return "";
}

// 等宽那一族：文件选择框的预览区、报告区这类地方用的是等宽字体。
// 它也要能画中文，否则文件名与预览内容会显示成方框。
std::string pick_mono_family() { return pick_family(); }

#else

// Linux 走 fontconfig：按语言要一款能显示简体中文的字体，让它自己挑最合适的。
std::string query_family(const char* query) {
    if (!FcInit()) return "";

    FcPattern* pattern = FcNameParse(reinterpret_cast<const FcChar8*>(query));
    if (pattern == nullptr) return "";
    FcConfigSubstitute(nullptr, pattern, FcMatchPattern);
    FcDefaultSubstitute(pattern);

    std::string family;
    FcResult result = FcResultNoMatch;
    FcPattern* match = FcFontMatch(nullptr, pattern, &result);
    if (match != nullptr) {
        FcChar8* name = nullptr;
        if (FcPatternGetString(match, FC_FAMILY, 0, &name) == FcResultMatch && name != nullptr) {
            family = reinterpret_cast<const char*>(name);
        }
        FcPatternDestroy(match);
    }
    FcPatternDestroy(pattern);
    return family;
}

// 按家族名问 fontconfig 要一款字体，回读到的名字与要的一致才算它找得到。
// 自带字体不在系统字体表里，只在本进程的字体配置里，所以要这样回问一句；
// 名字对不上就说明注册没生效，界面上会静默变回方框。
bool fc_finds_family(const char* family) {
    FcConfig* config = FcConfigGetCurrent();
    if (config == nullptr) return false;

    FcPattern* pattern = FcPatternCreate();
    if (pattern == nullptr) return false;
    FcPatternAddString(pattern, FC_FAMILY, reinterpret_cast<const FcChar8*>(family));
    FcConfigSubstitute(config, pattern, FcMatchPattern);
    FcDefaultSubstitute(pattern);

    bool found = false;
    FcResult result = FcResultNoMatch;
    FcPattern* match = FcFontMatch(config, pattern, &result);
    if (match != nullptr) {
        FcChar8* name = nullptr;
        if (FcPatternGetString(match, FC_FAMILY, 0, &name) == FcResultMatch && name != nullptr) {
            found = FcStrCmp(name, reinterpret_cast<const FcChar8*>(family)) == 0;
        }
        FcPatternDestroy(match);
    }
    FcPatternDestroy(pattern);
    return found;
}

// 把自带字体加进本进程的字体配置（不落到系统字体目录），加上了再确认家族名能查到。
bool register_bundled_font(const std::filesystem::path& file) {
    FcConfig* config = FcConfigGetCurrent();
    if (config == nullptr) return false;
    const std::string path = file.string();
    if (!FcConfigAppFontAddFile(config, reinterpret_cast<const FcChar8*>(path.c_str()))) {
        return false;
    }
    return fc_finds_family(kBundledFamily);
}

std::string pick_family() { return query_family("sans:lang=zh-cn"); }

// 等宽那一族：优先等宽的中文字体，系统没有就退回正文字体。
std::string pick_mono_family() {
    const std::string mono = query_family("monospace:lang=zh-cn");
    return mono.empty() ? pick_family() : mono;
}

#endif

// 界面最终用哪两款字体：正文字体与等宽字体。
struct SelectedFonts {
    std::string body;
    std::string mono;
};

// 先试自带字体，用不上再问系统。
SelectedFonts select_fonts(const std::string& program_path) {
    const std::filesystem::path bundled = find_bundled_font(program_path);
    if (!bundled.empty() && register_bundled_font(bundled)) {
        // 自带字体本身就是等宽的一款，正文与等宽两族用它同一份文件：
        // 少开一份字体文件，界面上的字宽只有一种。
        return {kBundledFamily, kBundledFamily};
    }

    SelectedFonts fallback;
    fallback.body = pick_family();
    fallback.mono = pick_mono_family();
    return fallback;
}

}  // namespace

std::string install_cjk_font(const std::string& program_path) {
    // 这两款字体名必须活到进程结束：Fl::set_font 只存指针，不复制字体名。
    // 用局部变量传 c_str() 进去，函数一返回指针就失效，界面上中文会全部变回方框，
    // 而且不报错、不崩溃，只是字形没了，很难往这里想。
    static SelectedFonts fonts = select_fonts(program_path);
    if (fonts.body.empty()) return "";

    // 四个正文字体位（正体、粗体、斜体、粗斜体）都指到同一款中文字体。
    // 不这么做的话，粗体控件会退回 FLTK 内置的英文字体，那一块的中文又是方框。
    const char* name = fonts.body.c_str();
    Fl::set_font(FL_HELVETICA, name);
    Fl::set_font(FL_HELVETICA_BOLD, name);
    Fl::set_font(FL_HELVETICA_ITALIC, name);
    Fl::set_font(FL_HELVETICA_BOLD_ITALIC, name);

    // 等宽那一族同样要换：文件选择框的预览区、报告区用的是它，
    // 只换正文字体时，这些地方的中文与文件名还是方框。
    const char* mono = (fonts.mono.empty() ? fonts.body : fonts.mono).c_str();
    Fl::set_font(FL_COURIER, mono);
    Fl::set_font(FL_COURIER_BOLD, mono);
    Fl::set_font(FL_COURIER_ITALIC, mono);
    Fl::set_font(FL_COURIER_BOLD_ITALIC, mono);
    return fonts.body;
}

}  // namespace texttool::ui
