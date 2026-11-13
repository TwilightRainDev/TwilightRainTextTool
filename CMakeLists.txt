cmake_minimum_required(VERSION 3.16)

# 原生版文本工具：引擎（检查与四个处理功能）、命令行、界面三块分开。
# 引擎不依赖界面库，命令行与界面都只连引擎，这样内存与性能的账都记在一处。
project(TextToolNative VERSION 4.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# 交付机器上没有 VC++ 运行库，Windows 侧一律静态运行库（/MT）。
# Linux 侧对应的是把 libstdc++ 与 libgcc 一起链进去，见下面的链接选项。
if(MSVC)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  add_compile_options(/utf-8 /W4 /permissive-)
else()
  add_compile_options(-Wall -Wextra)
  add_link_options(-static-libstdc++ -static-libgcc)
endif()

# 规则表在 assets/default_lint_rules.json，随仓库走，引擎不另存副本。
# 换机器或换目录时只改这一处；测试也读它，保证测的是真实数据。
# 构建时复制一份到程序旁边：打包与试跑都从程序所在目录找它，不依赖源码树。
set(TEXTTOOL_RULES_JSON
    "${CMAKE_CURRENT_SOURCE_DIR}/assets/default_lint_rules.json"
    CACHE FILEPATH "规则表 JSON 的路径")
set(TEXTTOOL_FIXTURES_DIR
    "${CMAKE_CURRENT_SOURCE_DIR}/fixtures"
    CACHE PATH "期望输出所在的目录")

# 随包带的默认角色表：引擎按「程序所在目录」找它，构建时复制到两个程序旁边。
set(TEXTTOOL_VN_SCHEMES_JSON
    "${CMAKE_CURRENT_SOURCE_DIR}/assets/default_vn_schemes.json")
if(NOT EXISTS "${TEXTTOOL_VN_SCHEMES_JSON}")
  message(FATAL_ERROR "找不到默认角色表：${TEXTTOOL_VN_SCHEMES_JSON}")
endif()

if(NOT EXISTS "${TEXTTOOL_RULES_JSON}")
  message(FATAL_ERROR "找不到规则表：${TEXTTOOL_RULES_JSON}")
endif()

add_library(engine STATIC
  src/engine/rules.cpp
  src/engine/rule_overrides.cpp
  src/engine/regex.cpp
  src/engine/needle_scan.cpp
  src/engine/encoding.cpp
  src/engine/report.cpp
  src/engine/location.cpp
  src/engine/lint_runner.cpp
  src/engine/lint_inputs.cpp
  src/engine/scan.cpp
  src/engine/algo_repeat.cpp
  src/engine/algo_rhythm.cpp
  src/engine/algo_shape.cpp
  src/engine/proc_text.cpp
  src/engine/markdown.cpp
  src/engine/io.cpp
  src/engine/calibration.cpp
  src/engine/reply_shape.cpp
  src/engine/proc_vn.cpp
  src/engine/schemes.cpp
  src/engine/config_pack.cpp
  src/engine/chain.cpp
)
target_include_directories(engine PUBLIC
  ${CMAKE_CURRENT_SOURCE_DIR}/src
  ${CMAKE_CURRENT_SOURCE_DIR}/third_party
)

# 检查那一层把规则分给几条线程各扫各的（见 src/engine/lint_runner.cpp），要用 std::thread。
# Windows 上不必另链，Linux 上要链 pthread，CMake 的 FindThreads 两边都替我们处理好。
set(THREADS_PREFER_PTHREAD_FLAG ON)
find_package(Threads REQUIRED)
target_link_libraries(engine PUBLIC Threads::Threads)

add_executable(texttool src/cli/main.cpp)
target_link_libraries(texttool PRIVATE engine)
# 命令行也要知道规则表在哪：装到别的机器上时用它，找不到再按运行时查找兜底。
# 版本号只有一处来源：上面 project() 那一行。命令行用法文本与界面关于页都用它，两处都不许再写死。
target_compile_definitions(texttool PRIVATE
  TEXTTOOL_RULES_JSON="${TEXTTOOL_RULES_JSON}"
  TEXTTOOL_VERSION="${PROJECT_VERSION}")


add_custom_command(TARGET texttool POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different "${TEXTTOOL_VN_SCHEMES_JSON}"
          "$<TARGET_FILE_DIR:texttool>"
  COMMAND ${CMAKE_COMMAND} -E copy_if_different "${TEXTTOOL_RULES_JSON}"
          "$<TARGET_FILE_DIR:texttool>"
  COMMENT "把默认角色表与规则表复制到程序旁边")

enable_testing()

add_executable(engine_tests
  tests/test_main.cpp
  tests/test_rules.cpp
  tests/test_rule_overrides.cpp
  tests/test_regex.cpp
  tests/test_encoding.cpp
  tests/test_io.cpp
  tests/test_report.cpp
  tests/test_location.cpp
  tests/test_lint_runner.cpp
  tests/test_lint_inputs.cpp
  tests/test_algo_repeat.cpp
  tests/test_algo_rhythm.cpp
  tests/test_algo_shape.cpp
  tests/test_proc_text.cpp
  tests/test_markdown.cpp
  tests/test_calibration.cpp
  tests/test_reply_shape.cpp
  tests/test_proc_vn.cpp
  tests/test_schemes.cpp
  tests/test_config_pack.cpp
  tests/test_chain.cpp
)
target_link_libraries(engine_tests PRIVATE engine)
target_compile_definitions(engine_tests PRIVATE
  TEXTTOOL_RULES_JSON="${TEXTTOOL_RULES_JSON}"
  TEXTTOOL_FIXTURES_DIR="${TEXTTOOL_FIXTURES_DIR}"
)
add_test(NAME engine_tests COMMAND engine_tests)

# 多步链的驱动：链还没挂到命令行与界面上，只有单测跑得到它，而 tests/compare_chain.sh
# 要用同一条链去比 fixtures/processing/chain 里的基准。只给对照脚本用，不进安装包。
add_executable(chain_runner tests/chain_runner.cpp)
target_link_libraries(chain_runner PRIVATE engine)

# 图形界面。FLTK 装在哪台机器上不一样，用 TEXTTOOL_FLTK_PREFIX 指过去：
# Linux 侧是 /home/twilightrain/Tools/fltk-1.4.5-linux，
# Windows 侧是自己编完再做一份安装布局（`cmake --install`）：FLTK 的构建目录里没有
# include/，直接指过去会找不到 FL/Fl.H。
# Windows 编译用 "NMake Makefiles"，静态运行库与 rint 那次修改见 tools/patches/fltk-msvc-rint.md。
#
# 找库有两条路，先走好的那条：
#   一、界面库自带的 CMake 配置包（安装目录里的 CMake/FLTKConfig.cmake 与 FLTK-Targets.cmake）。
#       它把每个库的绝对路径、头文件目录与互相之间的依赖都写好了，工程里就不必按平台写库名。
#       这一点在 Windows 上有用：PNG 与 zlib 通常不是系统库，是界面库自己带的那两份，
#       叫 fltk_png、fltk_z；Linux 上用的才是系统那两份，叫 png、z。
#   二、没有配置包（自己编完没 `cmake --install` 的那类布局）就手写查找，库名由下面按平台给。
option(TEXTTOOL_BUILD_GUI "构建图形界面" ON)

if(TEXTTOOL_BUILD_GUI)
  set(TEXTTOOL_FLTK_PREFIX "" CACHE PATH "FLTK 的安装目录，里面要有 include/FL 与 lib")

  # 配置包优先。HINTS 这两处是 FLTK 安装时写配置包的地方：<前缀>/CMake 与 <前缀>/lib/cmake/fltk。
  # 找得到就用它给的导入目标 fltk::images：头文件目录、库路径、zlib 与 PNG 的依赖都在它身上，
  # 图片部分（fltk::images）自己带着 fltk::fltk、fltk::png、fltk::jpeg。
  find_package(FLTK CONFIG QUIET
    HINTS "${TEXTTOOL_FLTK_PREFIX}" "${TEXTTOOL_FLTK_PREFIX}/CMake")

  if(TARGET fltk::images)
    message(STATUS "FLTK 用配置包：${FLTK_DIR}")
    set(TEXTTOOL_FLTK_TARGET fltk::images)
    set(TEXTTOOL_FLTK_LIBS "")
  else()
    find_path(TEXTTOOL_FLTK_INCLUDE FL/Fl.H HINTS "${TEXTTOOL_FLTK_PREFIX}/include")
    find_library(TEXTTOOL_FLTK_LIB NAMES fltk HINTS "${TEXTTOOL_FLTK_PREFIX}/lib")
    # 程序图标与作者头像要读 PNG，这两份静态库是界面库的图片部分。
    find_library(TEXTTOOL_FLTK_IMAGES_LIB NAMES fltk_images HINTS "${TEXTTOOL_FLTK_PREFIX}/lib")
    find_library(TEXTTOOL_FLTK_JPEG_LIB NAMES fltk_jpeg HINTS "${TEXTTOOL_FLTK_PREFIX}/lib")

    if(NOT TEXTTOOL_FLTK_INCLUDE OR NOT TEXTTOOL_FLTK_LIB)
      message(FATAL_ERROR
        "找不到 FLTK。用 -DTEXTTOOL_FLTK_PREFIX=<装了 FLTK 的目录> 指过去；"
        "不想要界面可以加 -DTEXTTOOL_BUILD_GUI=OFF。")
    endif()

    # 库名按平台给：界面库的图片部分自己不解 JPEG 与 PNG 压缩，底下还要压缩库。
    # Windows 上这两份是界面库自带的（fltk_png、fltk_z），Linux 上接系统的（png、z）。
    if(WIN32)
      set(TEXTTOOL_FLTK_COMPRESSION_LIBS fltk_png fltk_z)
    else()
      set(TEXTTOOL_FLTK_COMPRESSION_LIBS png z)
    endif()

    set(TEXTTOOL_FLTK_TARGET "")
    # 图片那两份库排在界面库前面：静态库按顺序解符号，排在后面就没人来取了。
    set(TEXTTOOL_FLTK_LIBS
      ${TEXTTOOL_FLTK_IMAGES_LIB} ${TEXTTOOL_FLTK_JPEG_LIB} ${TEXTTOOL_FLTK_LIB}
      ${TEXTTOOL_FLTK_COMPRESSION_LIBS})
  endif()

  add_executable(texttool-gui
    src/ui/app.cpp
    src/ui/cjk_font.cpp
    src/ui/theme.cpp
    src/ui/widgets.cpp
    src/ui/layout_check.cpp
    src/ui/page_process.cpp
    src/ui/page_merge.cpp
    src/ui/page_join.cpp
    src/ui/page_replace.cpp
    src/ui/page_vn.cpp
    src/ui/page_lint.cpp
    src/ui/page_about.cpp
  )
  if(NOT TEXTTOOL_FLTK_TARGET)
    target_include_directories(texttool-gui PRIVATE ${TEXTTOOL_FLTK_INCLUDE})
  endif()
  target_link_libraries(texttool-gui PRIVATE engine
    ${TEXTTOOL_FLTK_TARGET} ${TEXTTOOL_FLTK_LIBS})
  target_compile_definitions(texttool-gui PRIVATE
    TEXTTOOL_RULES_JSON="${TEXTTOOL_RULES_JSON}"
    TEXTTOOL_VERSION="${PROJECT_VERSION}")

  if(WIN32)
    # 双击就要开，不要多一个黑终端窗：声明成窗口子系统的程序。
    # 代价是标准输出与标准错误在 Windows 上没人看得见，所以界面上的错误一律走状态条或
    # 界面提示框（规则表读不出来这一类见 src/ui/app.cpp 启动那一段），不许只往标准错误写。
    # 这是 Windows 独有的一条，Linux 上照旧是控制台程序，自检与量内存那几样输出照常可见。
    set_target_properties(texttool-gui PROPERTIES WIN32_EXECUTABLE TRUE)
    # 界面这边有文件要直接带进 Windows 的头文件（ui/run_guard.h 探进程在不在、ui/cjk_font.cpp 读字体），
    # 那里面 min 与 max 是两个宏，会把 std::max 这一族拆成语法错误（C2589）。先掐掉这两个宏。
    target_compile_definitions(texttool-gui PRIVATE NOMINMAX)
    # 子系统换成窗口程序以后，链接器默认去找 WinMain 当入口，而工程的入口是 main。
    # 指回 CRT 的 mainCRTStartup，main 照旧是入口，不必为这件事再添一个只做转发的源文件。
    if(MSVC)
      target_link_options(texttool-gui PRIVATE /ENTRY:mainCRTStartup)
    endif()
  endif()

  # 界面字体：与程序放在同一个目录，程序启动时按这个位置找。
  # 名字必须与 src/ui/cjk_font.cpp 里的 kBundledFile 一致。
  set(TEXTTOOL_BUNDLED_FONT
      "${CMAKE_CURRENT_SOURCE_DIR}/assets/fonts/TextToolWenKai-Regular.ttf")
  if(NOT EXISTS "${TEXTTOOL_BUNDLED_FONT}")
    message(FATAL_ERROR "找不到界面字体：${TEXTTOOL_BUNDLED_FONT}")
  endif()
  add_custom_command(TARGET texttool-gui POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${TEXTTOOL_BUNDLED_FONT}"
            "$<TARGET_FILE_DIR:texttool-gui>"
    COMMENT "把界面字体复制到程序旁边")

  # 程序图标与作者头像：同样与程序放在同一个目录，程序启动时按这个位置找。
  # 名字必须与 src/ui/app.cpp 里的 kBundledIcon、page_about.cpp 里的 kAvatarFile 一致。
  set(TEXTTOOL_BUNDLED_IMAGES
      "${CMAKE_CURRENT_SOURCE_DIR}/assets/images/icon.png"
      "${CMAKE_CURRENT_SOURCE_DIR}/assets/images/avatar.png")
  foreach(TEXTTOOL_BUNDLED_IMAGE IN LISTS TEXTTOOL_BUNDLED_IMAGES)
    if(NOT EXISTS "${TEXTTOOL_BUNDLED_IMAGE}")
      message(FATAL_ERROR "找不到界面图片：${TEXTTOOL_BUNDLED_IMAGE}")
    endif()
  endforeach()
  add_custom_command(TARGET texttool-gui POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different ${TEXTTOOL_BUNDLED_IMAGES}
            "$<TARGET_FILE_DIR:texttool-gui>"
    COMMENT "把程序图标与作者头像复制到程序旁边")

add_custom_command(TARGET texttool-gui POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different "${TEXTTOOL_VN_SCHEMES_JSON}"
          "$<TARGET_FILE_DIR:texttool-gui>"
  COMMAND ${CMAKE_COMMAND} -E copy_if_different "${TEXTTOOL_RULES_JSON}"
          "$<TARGET_FILE_DIR:texttool-gui>"
  COMMENT "把默认角色表与规则表复制到程序旁边")

  if(WIN32)
    # FLTK 在 Windows 上还要这几个系统库，缺一个就是一大堆链接错误。
    # gdiplus 不能漏：界面库画抗锯齿线条与圆角要用它，缺了是三十多个未解析符号。
    target_link_libraries(texttool-gui PRIVATE
      comctl32 gdi32 gdiplus dwmapi user32 ws2_32 ole32 oleaut32 uuid advapi32 shell32 winspool)
  else()
    find_package(Threads REQUIRED)
    target_link_libraries(texttool-gui PRIVATE
      Xrender Xcursor Xfixes Xft fontconfig Xinerama Threads::Threads m X11)
  endif()
endif()
