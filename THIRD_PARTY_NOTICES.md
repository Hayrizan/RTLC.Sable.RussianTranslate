# Сторонние компоненты и ресурсы

- **BepInEx 6.0.0-pre.1 IL2CPP x64**: [релиз](https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.1), [исходники точной версии](https://github.com/BepInEx/BepInEx/tree/v6.0.0-pre.1). Лицензия LGPL 2.1 приведена в [third-party/LICENSE.BepInEx.txt](third-party/LICENSE.BepInEx.txt).
- Полный установочный ZIP содержит официальную поставку BepInEx с **UnityDoorstop, Mono, HarmonyX, MonoMod, Mono.Cecil, Cpp2IL, Il2CppAssemblyUnhollower** и их зависимостями. Бинарные зависимости в Git не хранятся. Ссылки на исходники компонентов: [BepInEx](https://github.com/BepInEx/BepInEx/tree/v6.0.0-pre.1), [Mono](https://github.com/BepInEx/mono), [Doorstop](https://github.com/NeighTools/UnityDoorstop).
- **Montserrat Regular/Medium/Bold** распространяются по SIL Open Font License 1.1. [Текст лицензии и уведомление об авторстве](Fonts/OFL-Montserrat.txt), [проект гарнитуры](https://github.com/JulietaUla/Montserrat).
- **EuclidFlex, EuclidTriangle, SangBleuVersailles** — гарнитуры оригинальной Sable. Их исходные OTF не публикуются в Git; инструмент сборки получает их из локальной копии игры. Готовый бандл сохраняет соответствующие игровые начертания. Права на эти гарнитуры и оригинальные ресурсы принадлежат их владельцам; новая лицензия на них данным проектом не предоставляется.
- Базовые библиотеки Unity для подготовки IL2CPP: [Unity Libs](https://unity.bepinex.dev/). Сгенерированные игровые сборки не публикуются в исходниках и установочном ZIP: они создаются локально при первом запуске.
- Русский перевод и изменённые логотипы предоставлены автором проекта RTLC TEAM. Публикация репозитория не изменяет права владельцев Sable и сторонних ресурсов.

Инструменты подготовки используют [UnityPy](https://github.com/K0lb3/UnityPy) и [Pillow](https://github.com/python-pillow/Pillow). Работа над кодом, редактурой и упаковкой выполнялась при участии OpenAI Codex.
