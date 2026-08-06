using NUnit.Framework;
using UnityEngine;

namespace ChartRunner.Tests
{
    /// <summary>
    /// ГЕЙТ ПРОТИВ МОЛЧА ВЫРЕЗАННОГО УПРАВЛЕНИЯ.
    ///
    /// Что случилось. Весь код ввода (`PlayInput`, `KeyboardBikeInput`, `TouchBikeInput`,
    /// переключатели телеметрии и трассы) обёрнут в `#if ENABLE_LEGACY_INPUT_MANAGER`.
    /// В настройках проекта стояло `activeInputHandler: 1` — только новый Input System, —
    /// поэтому символ не был определён, тела методов вырезал компилятор, и `PlayInput.Read()`
    /// всегда возвращал нули. Игра собиралась, запускалась, рисовала кадр и НЕ УПРАВЛЯЛАСЬ.
    /// На устройстве это выглядело как статичная картинка.
    ///
    /// Почему это не поймал ни один из 31 теста: все они подают ввод через
    /// `ScriptedBikeInput`, то есть в обход платформенного слоя. Тесты проверяли физику
    /// и ни разу — что до физики вообще доходит нажатие. Классическая дыра: покрыт весь
    /// код, кроме той единственной строки, которая соединяет игрока с игрой.
    ///
    /// Этот тест не проверяет физику. Он проверяет ровно одно: что ветка ввода
    /// СКОМПИЛИРОВАНА. Провалится, если кто-то вернёт `activeInputHandler` в 1.
    /// </summary>
    public class InputBackendGateTests
    {
        [Test]
        public void LegacyInputBackend_IsCompiledIn_OtherwiseTheGameHasNoControls()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            Assert.Pass("ENABLE_LEGACY_INPUT_MANAGER определён — код ввода в сборке есть.");
#else
            Assert.Fail(
                "ENABLE_LEGACY_INPUT_MANAGER НЕ определён. Весь код ввода вырезан препроцессором, "
                + "PlayInput.Read() возвращает нули, и игра неуправляема при формально успешной "
                + "сборке. Починка: ProjectSettings/ProjectSettings.asset → activeInputHandler: 2 "
                + "(Both). Значение 1 = только новый Input System.");
#endif
        }

        /// <summary>
        /// Второй гейт, независимый от первого: читает НАСТРОЙКУ, а не символ.
        ///
        /// Нужен потому, что первый тест проверяет состояние компиляции того же ассембли,
        /// в котором сам живёт. Если однажды тесты соберутся с другими символами, чем игра,
        /// он это пропустит. Здесь источник истины — файл настроек проекта.
        /// </summary>
        [Test]
        public void ProjectSetting_ActiveInputHandler_AllowsLegacy()
        {
            var path = System.IO.Path.Combine(Application.dataPath, "..",
                "ProjectSettings", "ProjectSettings.asset");
            Assert.IsTrue(System.IO.File.Exists(path), "не найден ProjectSettings.asset: " + path);

            var text = System.IO.File.ReadAllText(path);
            var m = System.Text.RegularExpressions.Regex.Match(text, @"activeInputHandler:\s*(\d)");
            Assert.IsTrue(m.Success, "в ProjectSettings.asset нет поля activeInputHandler");

            var value = int.Parse(m.Groups[1].Value);
            // 0 = только legacy, 1 = только новый Input System, 2 = оба.
            Assert.AreNotEqual(1, value,
                "activeInputHandler = 1 (только новый Input System) — код ввода игры написан "
                + "под legacy и будет вырезан препроцессором. Нужно 0 или 2.");
        }
    }
}
