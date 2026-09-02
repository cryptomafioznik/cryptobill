#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

namespace ChartRunner.EditorTools
{
    /// <summary>
    /// Info.plist для App Store после экспорта Xcode-проекта:
    ///  - ITSAppUsesNonExemptEncryption=false — только HTTPS, стандартная криптография
    ///    (иначе App Store Connect спрашивает экспорт-комплаенс при каждой загрузке);
    ///  - UIRequiresFullScreen / статус-бар — на всякий случай дублируем настройки.
    /// Правится файл сборки, не шаблон Unity — переживает любую пересборку.
    /// </summary>
    public class IosPlistPostprocess : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
            var path = Path.Combine(report.summary.outputPath, "Info.plist");
            if (!File.Exists(path)) return;
            var plist = new PlistDocument();
            plist.ReadFromFile(path);
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.root.SetBoolean("UIRequiresFullScreen", true);
            plist.root.SetBoolean("UIStatusBarHidden", true);
            plist.WriteToFile(path);
        }
    }
}
#endif
