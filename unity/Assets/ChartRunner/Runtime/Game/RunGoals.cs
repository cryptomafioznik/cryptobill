using System;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// ТРИ ЦЕЛИ НА ЗАЕЗД — модель Alto's Odyssey, взятая как есть, потому что она
    /// проверена рынком: у игры 4.8★ и репутация «невозможно оторваться» ровно из-за
    /// этой конструкции. Формулировка её авторов: «структура целей позволяет игроку
    /// исследовать бесконечный процедурный мир, оставляя ему маленькие достижимые
    /// задачи, которые держат его на земле».
    ///
    /// Почему именно она нам подходит: наша трасса ПРОЦЕДУРНАЯ и бесконечная — каждый
    /// заезд уникален, и без целей игроку нечего хотеть, кроме числа метров. Цели дают
    /// причину начать следующий заезд ДО того, как появится прогрессия и магазин.
    ///
    /// Три правила, скопированные вместе с моделью:
    ///  1. Цели видны ВСЕГДА и прогресс по ним идёт МЕЖДУ заездами (не сбрасывается).
    ///  2. Уровень закрывается, только когда выполнены все три — тогда выдаётся новая
    ///     тройка, сложнее. Это и есть длинная петля.
    ///  3. Цели разной природы: дистанция, деньги, трюк, риск. Одинаковые цели
    ///     превращают игру в один и тот же заезд.
    /// </summary>
    [Serializable]
    public class Goal
    {
        public string Text;
        public float Target;
        public float Progress;
        public bool Done => Progress >= Target;
        public float Fraction => Mathf.Clamp01(Progress / Mathf.Max(0.0001f, Target));

        /// <summary>Как показывать число: метры, штуки, секунды.</summary>
        public string Unit;

        public string Label => Text + "  " + Mathf.FloorToInt(Mathf.Min(Progress, Target))
                               + "/" + Mathf.RoundToInt(Target) + Unit;
    }

    public static class RunGoals
    {
        private const string KeyLevel = "cr_goal_level";
        private const string KeyProgress = "cr_goal_progress_";

        /// <summary>Текущий уровень целей. Растёт, когда закрыта вся тройка.</summary>
        public static int Level { get; private set; }

        public static readonly Goal[] Active = new Goal[3];

        /// <summary>Показать плашку «уровень закрыт» до этого момента времени.</summary>
        public static float LevelUpBannerUntil;

        static RunGoals() => Load();

        private static void Load()
        {
            Level = PlayerPrefs.GetInt(KeyLevel, 1);
            Build(Level);
            for (var i = 0; i < 3; i++)
                Active[i].Progress = PlayerPrefs.GetFloat(KeyProgress + i, 0f);
        }

        private static void Save()
        {
            PlayerPrefs.SetInt(KeyLevel, Level);
            for (var i = 0; i < 3; i++) PlayerPrefs.SetFloat(KeyProgress + i, Active[i].Progress);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Тройка целей уровня. Растёт по уровню, но остаётся достижимой за 1-3 заезда:
        /// цель, требующая десяти заездов, перестаёт быть целью и становится фоном.
        /// Три разные природы в каждой тройке — по правилу 3.
        /// </summary>
        private static void Build(int level)
        {
            var k = 1f + (level - 1) * 0.35f;

            // Ротация по природе задачи, чтобы тройки не повторялись подряд.
            var distTargets = new[] { 150f, 250f, 400f, 600f };
            var coinTargets = new[] { 8f, 15f, 25f, 40f };
            var trickTargets = new[] { 2.5f, 4f, 6f, 9f };

            var d = distTargets[(level - 1) % distTargets.Length] * k;
            var c = coinTargets[(level - 1) % coinTargets.Length] * k;
            var s = trickTargets[(level - 1) % trickTargets.Length] * k;

            Active[0] = new Goal { Text = "ПРОЕХАТЬ", Target = Mathf.Round(d / 10f) * 10f, Unit = " м" };
            Active[1] = new Goal { Text = "СОБРАТЬ", Target = Mathf.Round(c), Unit = " ◆" };

            // Третья цель чередуется: воздух / вилли / отрыв от волны — чтобы игрок
            // осваивал разные навыки, а не повторял один.
            switch (level % 3)
            {
                case 0:
                    Active[2] = new Goal
                    {
                        Text = "В ВОЗДУХЕ", Target = Mathf.Round(s * 10f) / 10f, Unit = " с"
                    };
                    break;
                case 1:
                    Active[2] = new Goal
                    {
                        Text = "НА ЗАДНЕМ", Target = Mathf.Round(s * 10f) / 10f, Unit = " с"
                    };
                    break;
                default:
                    Active[2] = new Goal
                    {
                        Text = "ОТРЫВ ОТ ВОЛНЫ", Target = Mathf.Round(s * 4f), Unit = " м"
                    };
                    break;
            }
        }

        // ---- вклад заезда ----

        public static void AddDistance(float m) => Add(0, m);
        public static void AddCoins(int n) => Add(1, n);
        public static void AddSkill(float v) => Add(2, v);

        private static void Add(int i, float v)
        {
            if (v <= 0f) return;
            var g = Active[i];
            if (g.Done) return;
            g.Progress += v;
        }

        /// <summary>
        /// Итог заезда: сохранить прогресс и, если закрыта вся тройка, поднять уровень.
        /// Вызывается на смерти — прогресс не теряется, это и держит следующий заезд.
        /// Возвращает true, если уровень закрыт.
        /// </summary>
        public static bool CommitRun()
        {
            var all = Active[0].Done && Active[1].Done && Active[2].Done;
            if (all)
            {
                Level++;
                Build(Level);
                for (var i = 0; i < 3; i++) Active[i].Progress = 0f;
                LevelUpBannerUntil = Time.time + 2.6f;
            }
            Save();
            return all;
        }

        /// <summary>Полный сброс — для отладки и для проверки первого опыта игрока.</summary>
        public static void ResetAll()
        {
            Level = 1;
            Build(1);
            for (var i = 0; i < 3; i++) Active[i].Progress = 0f;
            Save();
        }
    }
}
