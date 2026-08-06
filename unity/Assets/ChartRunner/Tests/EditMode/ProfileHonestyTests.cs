using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ChartRunner.Tuning;
using NUnit.Framework;
using UnityEngine;

namespace ChartRunner.Tests
{
    /// <summary>
    /// ГЕЙТ ПРОТИВ ДЕКОРАТИВНОЙ КОНФИГУРАЦИИ.
    ///
    /// Требование: каждое сериализованное поле профиля обязано либо ЧИТАТЬСЯ рантайм-кодом,
    /// либо быть явно перечисленным в одном из двух списков — `supersededByEmergentPhysics`
    /// (в Unity получается само) или `notImplementedYet` (честный пробел).
    ///
    /// Зачем этот тест существует. В исходной игре нашлась константа `TUNE.launch = 2.0` с
    /// подробным комментарием про центробежный отрыв — и она НИГДЕ НЕ ЧИТАЛАСЬ, оставшись от
    /// модели, заменённой давно. Я на этом ошибся: приписал ей причину неработающего прыжка.
    /// А через два часа обнаружил, что воспроизвёл тот же дефект в своём коде: 8 полей из 57
    /// выглядели настройкой и ничего не делали, включая `climbGrip` — эндуро-бонус сцепления,
    /// то есть заявленную механику ядра.
    ///
    /// Поле, которое выглядит настройкой и ею не является, обманывает читателя сильнее, чем
    /// отсутствующее поле. Поэтому проверка автоматическая, а не «надо не забывать».
    /// </summary>
    public class ProfileHonestyTests
    {
        private static string RuntimeDir =>
            Path.Combine(Application.dataPath, "ChartRunner", "Runtime");

        private static string ProfileSourcePath =>
            Path.Combine(RuntimeDir, "Tuning", "BikeTuningProfile.cs");

        [Test]
        public void EverySerializedField_IsEitherRead_OrExplicitlyDeclaredUnused()
        {
            Assert.IsTrue(Directory.Exists(RuntimeDir), "не найдена папка рантайма: " + RuntimeDir);

            // Исходники рантайма, КРОМЕ самого профиля: объявление полем себя не читает.
            var runtimeText = string.Join("\n",
                Directory.GetFiles(RuntimeDir, "*.cs", SearchOption.AllDirectories)
                    .Where(p => !p.EndsWith("BikeTuningProfile.cs"))
                    .Select(File.ReadAllText));
            Assert.IsNotEmpty(runtimeText, "рантайм-исходники не прочитались — тест был бы пустым");

            var profile = ScriptableObject.CreateInstance<BikeTuningProfile>();
            var declaredUnused = new HashSet<string>(
                (profile.supersededByEmergentPhysics ?? new string[0])
                .Concat(profile.notImplementedYet ?? new string[0]));

            // Мета-поля: они и есть механизм честности, читать их рантайму незачем.
            var meta = new HashSet<string>
            {
                nameof(BikeTuningProfile.pendingCalibration),
                nameof(BikeTuningProfile.supersededByEmergentPhysics),
                nameof(BikeTuningProfile.notImplementedYet)
            };

            var fields = typeof(BikeTuningProfile)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(f => f.Name)
                .Where(n => !meta.Contains(n))
                .ToList();

            Assert.Greater(fields.Count, 30, "поля профиля не нашлись — тест не смог бы провалиться");

            var silent = new List<string>();

            // ВТОРАЯ СТОРОНА ГЕЙТА. Поле, объявленное неиспользуемым, но на самом деле
            // читаемое, обманывает ровно так же, как молчащее: конфиг утверждает про себя
            // неправду. Проверка добавлена после того, как `climbGrip` была реализована —
            // без неё пометка `notImplementedYet: climbGrip` осталась бы висеть и врать,
            // и никакой тест бы этого не заметил.
            var lyingUnused = new List<string>();

            foreach (var f in fields)
            {
                // Ищем обращение вида `.<имя>` — то есть чтение через ссылку на профиль.
                var read = Regex.IsMatch(runtimeText, @"\." + Regex.Escape(f) + @"\b");
                if (!read && !declaredUnused.Contains(f)) silent.Add(f);
                if (read && declaredUnused.Contains(f)) lyingUnused.Add(f);
            }

            Assert.IsEmpty(lyingUnused,
                "поля объявлены неиспользуемыми, но рантайм их ЧИТАЕТ — пометка врёт: "
                + string.Join(", ", lyingUnused));

            Debug.Log("PROFILE HONESTY\n  полей (без мета): " + fields.Count
                      + "\n  объявлено эмерджентными: " + declaredUnused.Count
                      + "\n  молчащих (ни чтения, ни объявления): " + silent.Count
                      + (silent.Count > 0 ? "\n    " + string.Join(", ", silent) : ""));

            Assert.IsEmpty(silent,
                "эти поля выглядят настройкой, но рантайм их не читает и они не объявлены "
                + "неиспользуемыми: " + string.Join(", ", silent)
                + ". Либо реализовать, либо внести в supersededByEmergentPhysics / notImplementedYet.");
        }

        [Test]
        public void DeclaredUnusedLists_ReferenceRealFields()
        {
            // Список пробелов бесполезен, если в нём опечатки: он тихо разойдётся с кодом.
            var profile = ScriptableObject.CreateInstance<BikeTuningProfile>();
            var real = new HashSet<string>(typeof(BikeTuningProfile)
                .GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name));

            foreach (var pair in new[]
                     {
                         ("supersededByEmergentPhysics", profile.supersededByEmergentPhysics),
                         ("notImplementedYet", profile.notImplementedYet),
                         ("pendingCalibration", profile.pendingCalibration)
                     })
            {
                foreach (var name in pair.Item2 ?? new string[0])
                    Assert.Contains(name, real.ToList(),
                        "в списке " + pair.Item1 + " указано несуществующее поле «" + name + "»");
            }
        }

        [Test]
        public void ProfileSource_DocumentsWhyNumbersAreNotPortableVerbatim()
        {
            // Профиль — первое, что откроет человек. Предупреждение о том, что часть чисел
            // исходника НЕ является физическими величинами, обязано быть в нём, а не только
            // в документе, который можно не прочитать.
            var text = File.ReadAllText(ProfileSourcePath);
            foreach (var marker in new[] { "ПЛЕЙСХОЛДЕР", "ОТКАЛИБРОВАНО", "BIKE_PHYSICS_SPEC" })
                Assert.IsTrue(text.Contains(marker),
                    "в исходнике профиля нет маркера «" + marker + "»");
        }
    }
}
