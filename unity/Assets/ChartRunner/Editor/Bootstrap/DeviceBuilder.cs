using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace ChartRunner.EditorTools
{
    /// <summary>
    /// Сборка играбельных билдов из batchmode: мак — для быстрой проверки, iOS — для телефона.
    ///
    /// Порядок не случаен. Мак-билд компилируется на Mono за десятки секунд, iOS идёт через
    /// Il2CPP и занимает минуты, поэтому «работает ли игра вообще» проверяется на маке, а на
    /// телефон уезжает то, что уже проверено. Обратный порядок стоит десяти минут за каждую
    /// опечатку.
    ///
    /// Настройки подписи ставятся здесь, а не руками в инспекторе: настройка, которую можно
    /// потерять при переключении платформы, обязана быть в коде.
    /// </summary>
    public static class DeviceBuilder
    {
        private const string BundleId = "com.mathewk.chartrunner";
        private const string TeamId = "R36SPNVQUW";
        private const string ProductName = "ChartRunner";

        private static readonly List<string> Lines = new List<string>();

        /// <summary>-buildTarget OSXUniversal -executeMethod ChartRunner.EditorTools.DeviceBuilder.BuildMac</summary>
        public static void BuildMac()
        {
            var outDir = Path.Combine(Directory.GetCurrentDirectory(), "build", "mac");
            Directory.CreateDirectory(outDir);
            var appPath = Path.Combine(outDir, ProductName + ".app");

            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = "Mathew K";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            // Окно в пропорциях телефона: судить композицию по ландшафтному окну нельзя,
            // это ровно тот способ обмануть себя, за который проект уже платил.
            PlayerSettings.defaultScreenWidth = 430;
            PlayerSettings.defaultScreenHeight = 932;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;

            Run(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, appPath, "mac");
        }

        /// <summary>-buildTarget iOS -executeMethod ChartRunner.EditorTools.DeviceBuilder.BuildIos</summary>
        public static void BuildIos()
        {
            var outDir = Path.Combine(Directory.GetCurrentDirectory(), "build", "ios");

            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = "Mathew K";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);

            PlayerSettings.iOS.appleDeveloperTeamID = TeamId;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.appInBackgroundBehavior = iOSAppInBackgroundBehavior.Suspend;

            // Портрет и только портрет: композиция считается под 430×932, и автоповорот
            // молча превратил бы проверку кадра в проверку другого кадра.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.useAnimatedAutorotation = false;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.iOS,
                Il2CppCompilerConfiguration.Debug);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Minimal);

            // Инкрементальный Xcode-проект: append переиспользует прошлую сборку и режет
            // время повторного круга в разы. При первом заходе папки нет — тогда replace.
            var append = Directory.Exists(Path.Combine(outDir, "Unity-iPhone.xcodeproj"));
            Directory.CreateDirectory(outDir);
            Run(BuildTarget.iOS, BuildTargetGroup.iOS, outDir, "ios",
                append ? BuildOptions.AcceptExternalModificationsToPlayer : BuildOptions.None);
        }

        private static void Run(BuildTarget target, BuildTargetGroup group, string path, string tag,
            BuildOptions options = BuildOptions.None)
        {
            Lines.Add("=== СБОРКА " + tag.ToUpperInvariant() + " ===");
            Lines.Add("Unity " + Application.unityVersion);

            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled) scenes.Add(s.path);

            if (scenes.Count == 0)
            {
                Fail("в билде нет ни одной сцены — сначала PlaySceneBuilder.Build");
                return;
            }
            Lines.Add("сцены: " + string.Join(", ", scenes));
            Lines.Add("цель: " + path);

            // Без BuildOptions.Development: плашка «Development Build» в углу читается
            // прототипом, а мы судим картинку. Отладчик на девайсе не используется —
            // диагностика идёт логами и кадрами.
            var opts = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = path,
                target = target,
                targetGroup = group,
                options = options
            };

            var report = BuildPipeline.BuildPlayer(opts);
            var summary = report.summary;

            Lines.Add("результат: " + summary.result);
            Lines.Add("время: " + summary.totalTime);
            Lines.Add("размер: " + summary.totalSize + " байт");
            Lines.Add("ошибок: " + summary.totalErrors + ", предупреждений: " + summary.totalWarnings);

            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var msg in step.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            Lines.Add("  ERR " + msg.content);
                Fail("сборка не удалась");
                return;
            }

            Lines.Add("BUILD RESULT: PASS — " + path);
            Flush(tag);
            EditorApplication.Exit(0);
        }

        private static void Fail(string why)
        {
            Lines.Add("BUILD RESULT: FAIL — " + why);
            Flush("fail");
            EditorApplication.Exit(1);
        }

        private static void Flush(string tag)
        {
            foreach (var l in Lines) Debug.Log(l);
            try
            {
                var dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
                Directory.CreateDirectory(dir);
                File.WriteAllLines(Path.Combine(dir, "build-" + tag + ".txt"), Lines);
            }
            catch (Exception e)
            {
                Debug.LogWarning("не удалось записать лог сборки: " + e.Message);
            }
        }
    }
}
