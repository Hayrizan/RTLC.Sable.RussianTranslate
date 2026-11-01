"""Create a root-extractable Windows x64 distribution from pinned dependencies."""
from pathlib import Path, PurePosixPath
import configparser
import argparse
import hashlib
import json
import zipfile

ROOT = Path(__file__).resolve().parent.parent
VERSION = "2.3.1"
DEPS = ROOT / "build/dependencies"
BASE = DEPS / "BepInEx_UnityIL2CPP_x64_6.0.0-pre.1.zip"
BASE_SHA256 = "b5ac213ee70b029bda1353a82b067d89709ec1a55ec9b775779c2813f3d84212"
STAGE = ROOT / f"build/release/Sable_Russian_{VERSION}_Windows_x64"
OUTPUT = ROOT / "dist" / f"Sable_Russian_{VERSION}_BepInEx_Windows_x64.zip"

CONFIG = """[Caching]
EnableAssemblyCache = true

[Harmony.Logger]
LogChannels = Warn, Error

[IL2CPP]
UnityVersion = 2020.3.35
UpdateUnhollowedAssemblies = true
# Base libraries are included below; empty source disables network download.
UnityBaseLibrariesSource =
Il2CppDumperType = Cpp2IL
DumpDummyAssemblies = false
UnhollowedAssembliesPath = {BepInEx}

[Logging]
UnityLogListening = true

[Logging.Console]
Enabled = false

[Logging.Disk]
WriteUnityLog = true
AppendLog = false
Enabled = true
LogLevels = Fatal, Error, Warning, Message
InstantFlushing = false
ConcurrentFileLimit = 5

[Preloader]
HarmonyBackend = auto
DumpAssemblies = false
LoadDumpedAssemblies = false
BreakBeforeLoadAssemblies = false
"""

README = """SABLE — РУССКИЙ ПЕРЕВОД 2.3.1
Windows x64 / Steam, Unity 2020.3.35f1

УСТАНОВКА
1. Закройте Sable.
2. Распакуйте содержимое этого ZIP в папку с Sable.exe.
   В Steam: Свойства игры → Установленные файлы → Обзор.
   Папки BepInEx и mono, файлы winhttp.dll и doorstop_config.ini должны
   оказаться рядом с Sable.exe, без дополнительной вложенной папки.
3. Запустите игру обычным способом через Steam.

При первом запуске BepInEx создаст библиотеки совместимости для вашей
установки игры. Этот запуск может занять заметно больше времени; дождитесь
главного меню. Необходимые базовые библиотеки Unity уже включены в архив,
их скачивание отключено. Дополнительные программы устанавливать не нужно.

На первом запуске русификатора выбирается русский язык. Если вы уже
пользовались русификатором, сохраняется ваш предыдущий выбор языка.
При необходимости смените язык в настройках игры.

В папке русификатора находятся только:
BepInEx/plugins/RTLC.Sable.RussianTranslate/RTLC.Sable.RussianTranslate.dll
BepInEx/plugins/RTLC.Sable.RussianTranslate/rtlc.bundle

ВАЖНО ДЛЯ СУЩЕСТВУЮЩИХ МОДОВ
Архив включает BepInEx 6.0.0-pre.1 IL2CPP x64 и его настройки. Он рассчитан
на чистую игру или такую же версию загрузчика. Перед установкой поверх
других модов сохраните их загрузчик и конфигурацию: файлы совпадающих имён
будут заменены. Архив не содержит ваших сохранений и не заменяет их.

ЕСЛИ ПЕРЕВОД НЕ ПОЯВИЛСЯ
Проверьте, что winhttp.dll лежит непосредственно рядом с Sable.exe,
а DLL и rtlc.bundle — в указанной выше папке. Журнал диагностики:
BepInEx/LogOutput.log. Версия игры с другим Unity и другие загрузчики
не проверялись. Консоль отключена, запись лога буферизуется.

УДАЛЕНИЕ
Сначала выберите English в настройках и выйдите из игры. Для удаления
только русификатора удалите папку RTLC.Sable.RussianTranslate из plugins.
Если других модов нет, можно также удалить установленные из архива
BepInEx, mono, winhttp.dll и doorstop_config.ini. Не удаляйте общие файлы
загрузчика, если они нужны другим модам. Сохранения игры не удаляйте.

АВТОРСТВО И КОМПОНЕНТЫ
Русификатор: RTLC TEAM. Логотипы предоставлены автором проекта.
Код и упаковка дорабатывались с помощью ИИ-ассистента OpenAI Codex.
BepInEx и сторонние компоненты: см. RTLC_README/THIRD_PARTY_NOTICES.txt.

ПРОВЕРКА ЭТОГО ПАКЕТА
Проверены сборка DLL, регрессионные тесты, загрузка ассетов в Unity,
состав и целостность ZIP. Холодный запуск именно этого ZIP на чистой
установке Sable не выполнялся. Первый запуск внутри игры требует проверки.
"""

NOTICES = """Third-party components

BepInEx 6.0.0-pre.1, Windows x64 IL2CPP (official unmodified distribution):
https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.1
Source corresponding to the bundled BepInEx binaries:
https://github.com/BepInEx/BepInEx/tree/v6.0.0-pre.1
BepInEx license: GNU LGPL 2.1, see LICENSE.BepInEx.txt.

The upstream distribution includes UnityDoorstop, Mono, HarmonyX,
MonoMod, Mono.Cecil, Il2CppAssemblyUnhollower, Cpp2IL, Il2CppDumper,
Capstone integration and supporting .NET libraries. These components
remain the property of their respective copyright holders and retain
their upstream licenses. Upstream source/dependency references:
https://github.com/BepInEx/BepInEx/blob/v6.0.0-pre.1/README.md
https://github.com/BepInEx/BepInEx/tree/v6.0.0-pre.1/BepInEx.IL2CPP
https://github.com/BepInEx/mono
https://github.com/NeighTools/UnityDoorstop

Unity base libraries for Unity 2020.3.35, used for first-run proxy generation:
https://unity.bepinex.dev/
The working local BepInEx unity-libs set is included without generated
game proxy assemblies. Copyright Unity Technologies and contributors.

Fonts and logos are included in rtlc.bundle. No license for the plugin
code or these assets is newly granted by this packaging operation.
"""


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity-libs', required=True, type=Path,
                        help='Unity 2020.3.35 base-library directory (BepInEx/unity-libs from a working local setup)')
    args = parser.parse_args()
    assert BASE.is_file(), "Download the pinned official BepInEx distribution first"
    assert sha(BASE.read_bytes()) == BASE_SHA256, "BepInEx dependency does not match the pinned official distribution"
    assert not STAGE.exists(), "Staging directory already exists; use a new clean staging directory"
    files = {}
    with zipfile.ZipFile(BASE) as archive:
        assert archive.testzip() is None
        for item in archive.infolist():
            if item.is_dir():
                continue
            name = item.filename.replace("\\", "/")
            assert not PurePosixPath(name).is_absolute() and ".." not in PurePosixPath(name).parts
            files[name] = archive.read(item)
    # All runtime files originate in the pristine upstream archive.
    files["RTLC_README/BepInEx-changelog.txt"] = files.pop("changelog.txt")
    files["BepInEx/config/BepInEx.cfg"] = CONFIG.encode("utf-8")
    libs = sorted(args.unity_libs.glob("UnityEngine*.dll"))
    assert len(libs) == 41, "Unexpected Unity base-library inventory"
    for lib in libs:
        files["BepInEx/unity-libs/" + lib.name] = lib.read_bytes()
    plugin = "BepInEx/plugins/RTLC.Sable.RussianTranslate/"
    files[plugin + "RTLC.Sable.RussianTranslate.dll"] = (ROOT / "bin/Release/netstandard2.1/RTLC.Sable.RussianTranslate.dll").read_bytes()
    files[plugin + "rtlc.bundle"] = (ROOT / "build/bundle/rtlc.bundle").read_bytes()
    files["README_RU.txt"] = README.encode("utf-8-sig")
    files["RTLC_README/THIRD_PARTY_NOTICES.txt"] = NOTICES.encode("utf-8")
    files["RTLC_README/LICENSE.BepInEx.txt"] = (ROOT / "third-party/LICENSE.BepInEx.txt").read_bytes()
    files["RTLC_README/LICENSE.Montserrat.txt"] = (ROOT / "Fonts/OFL-Montserrat.txt").read_bytes()
    cfg = configparser.ConfigParser(interpolation=None)
    cfg.read_string(CONFIG)
    assert cfg["IL2CPP"]["UnityBaseLibrariesSource"] == ""
    assert cfg["IL2CPP"].getboolean("UpdateUnhollowedAssemblies")
    assert not cfg["Logging.Console"].getboolean("Enabled")
    assert not cfg["Logging.Disk"].getboolean("InstantFlushing")
    doorstop = configparser.ConfigParser()
    doorstop.read_string(files["doorstop_config.ini"].decode("utf-8-sig"))
    assert doorstop["UnityDoorstop"].getboolean("enabled")
    for section, key in [("UnityDoorstop", "targetAssembly"), ("MonoBackend", "runtimeLib")]:
        assert doorstop[section][key].replace("\\", "/") in files
    assert sorted(name[len(plugin):] for name in files if name.startswith(plugin)) == ["RTLC.Sable.RussianTranslate.dll", "rtlc.bundle"]
    banned = {"gameassembly.dll", "global-metadata.dat", "assembly-csharp.dll", "sable.exe", "unityplayer.dll", "assembly-hash.txt"}
    for name in files:
        assert PurePosixPath(name).name.lower() not in banned, name
        assert not any(part.lower() in {"unhollowed", "cache", "dummy", "sable_data", "saves", "savedata"} for part in PurePosixPath(name).parts), name
        assert not name.lower().endswith((".log", ".pdb")), name
    manifest = {"version": VERSION, "bepinex_source_sha256": sha(BASE.read_bytes()),
                "files": {name: sha(data) for name, data in sorted(files.items())}}
    files["RTLC_README/SHA256.json"] = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    for name, data in files.items():
        target = STAGE / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    assert not OUTPUT.exists(), "Refusing to overwrite an existing release ZIP"
    with zipfile.ZipFile(OUTPUT, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(files.items()):
            archive.writestr(name, data)
    with zipfile.ZipFile(OUTPUT) as archive:
        assert archive.testzip() is None
        assert len(archive.namelist()) == len(set(archive.namelist())) == len(files)
        for name, data in files.items():
            assert archive.read(name) == data, name
    checksum = sha(OUTPUT.read_bytes())
    OUTPUT.with_suffix(".zip.sha256").write_text(checksum + "  " + OUTPUT.name + "\n", encoding="ascii")
    report = {"archive": str(OUTPUT), "size_bytes": OUTPUT.stat().st_size, "sha256": checksum,
              "files": len(files), "unity_base_libraries": len(libs), "stage": str(STAGE),
              "zip_crc_and_contents_verified": True, "cold_game_launch_tested": False}
    (OUTPUT.parent / "release-verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
