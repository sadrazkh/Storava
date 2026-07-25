using Storava.Domain.Enums;
using Storava.Rules.Model;

namespace Storava.Rules.Catalog;

/// <summary>Temporary files, logs, crash dumps, browser caches and generic user folders.</summary>
internal static class SystemRules
{
    internal static IEnumerable<StorageRule> All()
    {
        yield return new StorageRule
        {
            Id = "system.temp",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Temporary files",
                ["fa"] = "فایل‌های موقت"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Scratch files written by Windows and applications. Anything still in use is locked and simply skipped.",
                ["fa"] = "فایل‌های موقتی که ویندوز و برنامه‌ها می‌نویسند. هر چیزی که در حال استفاده باشد قفل است و به‌سادگی رد می‌شود."
            },
            Patterns =
            [
                new RulePattern(@"AppData\Local\Temp", RuleMatchTarget.PathSuffix),
                new RulePattern(@"Windows\Temp", RuleMatchTarget.PathSuffix)
            ],
            Category = StorageCategory.TemporaryFiles,
            RiskLevel = RiskLevel.Low,
            CanDelete = true,
            CanMove = false,
            CanRegenerate = true,
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "Close running applications first so more of it can be reclaimed.",
                ["fa"] = "ابتدا برنامه‌های در حال اجرا را ببندید تا بخش بیشتری آزاد شود."
            },
            Confidence = 0.96
        };

        yield return new StorageRule
        {
            Id = "system.crash-dumps",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Crash dumps",
                ["fa"] = "فایل‌های Crash Dump"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Memory snapshots written when an application or Windows crashed. Only useful while diagnosing that crash.",
                ["fa"] = "تصویر حافظه که هنگام کرش یک برنامه یا ویندوز نوشته شده است. فقط در زمان بررسی همان کرش کاربرد دارد."
            },
            Patterns =
            [
                new RulePattern("CrashDumps"),
                new RulePattern("Minidump"),
                new RulePattern("LiveKernelReports"),
                new RulePattern("MEMORY.DMP", RuleMatchTarget.Name, ItemType.File)
            ],
            Category = StorageCategory.TemporaryFiles,
            RiskLevel = RiskLevel.Low,
            CanDelete = true,
            CanMove = false,
            CanRegenerate = false,
            Confidence = 0.93
        };

        yield return new StorageRule
        {
            Id = "system.windows-update-download",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Windows Update downloads",
                ["fa"] = "دانلودهای Windows Update"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Update packages already installed or pending. Windows re-downloads whatever it still needs.",
                ["fa"] = "بسته‌های به‌روزرسانی که نصب شده یا در انتظارند. ویندوز هر چیزی را که لازم باشد دوباره دانلود می‌کند."
            },
            Patterns = [new RulePattern(@"SoftwareDistribution\Download", RuleMatchTarget.PathSuffix)],
            Category = StorageCategory.TemporaryFiles,
            Technology = "Windows Update",
            RiskLevel = RiskLevel.Medium,
            CanDelete = true,
            CanMove = false,
            CanRegenerate = true,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            OfficialMigrationHint = "Use Settings → System → Storage → Cleanup recommendations, or Disk Cleanup, rather than deleting by hand.",
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "Prefer the built-in Windows cleanup tools for this location.",
                ["fa"] = "برای این مسیر بهتر است از ابزارهای پاک‌سازی خود ویندوز استفاده کنید."
            },
            Confidence = 0.9
        };

        yield return new StorageRule
        {
            Id = "browser.cache",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Browser cache",
                ["fa"] = "کش مرورگر"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Cached web content for Chrome, Edge, Firefox and other browsers. Refilled as you browse; bookmarks and passwords are untouched.",
                ["fa"] = "محتوای وب ذخیره‌شده برای کروم، اج، فایرفاکس و مرورگرهای دیگر. با مرور دوباره پر می‌شود؛ بوکمارک‌ها و رمزها دست‌نخورده می‌مانند."
            },
            Patterns =
            [
                new RulePattern(@"User Data\Default\Cache", RuleMatchTarget.PathSuffix),
                new RulePattern("Code Cache"),
                new RulePattern("GPUCache"),
                new RulePattern(@"Profiles\cache2", RuleMatchTarget.PathContains)
            ],
            Category = StorageCategory.BrowserCaches,
            RiskLevel = RiskLevel.Low,
            CanDelete = true,
            CanMove = false,
            CanRegenerate = true,
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "Close the browser first; sites will load slightly slower the first time afterwards.",
                ["fa"] = "ابتدا مرورگر را ببندید؛ بار اول سایت‌ها کمی کندتر بارگذاری می‌شوند."
            },
            Confidence = 0.9
        };

        yield return new StorageRule
        {
            Id = "system.logs",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Log files",
                ["fa"] = "فایل‌های لاگ"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Diagnostic logs written by applications. Useful for troubleshooting recent problems only.",
                ["fa"] = "لاگ‌های تشخیصی که برنامه‌ها می‌نویسند. فقط برای بررسی مشکلات اخیر کاربرد دارند."
            },
            Patterns =
            [
                new RulePattern("logs"),
                new RulePattern("Logs")
            ],
            Category = StorageCategory.Logs,
            RiskLevel = RiskLevel.Low,
            CanDelete = true,
            CanMove = false,
            CanRegenerate = true,
            Confidence = 0.6
        };

        yield return new StorageRule
        {
            Id = "user.downloads",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Downloads folder",
                ["fa"] = "پوشه‌ی دانلودها"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Files you downloaded. Often full of installers that are no longer needed, but only you know what matters here.",
                ["fa"] = "فایل‌هایی که دانلود کرده‌اید. اغلب پر از نصب‌کننده‌هایی است که دیگر لازم نیستند، اما فقط خودتان می‌دانید چه چیزی مهم است."
            },
            Patterns = [new RulePattern("Downloads")],
            Category = StorageCategory.Downloads,
            RiskLevel = RiskLevel.High,
            CanDelete = false,
            CanMove = true,
            CanRegenerate = false,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            OfficialMigrationHint = "Right-click the folder → Properties → Location → Move, so Windows updates the known-folder path.",
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "Personal files. Review the contents yourself before doing anything.",
                ["fa"] = "فایل‌های شخصی. پیش از هر کاری خودتان محتوا را بررسی کنید."
            },
            Confidence = 0.85
        };

        yield return new StorageRule
        {
            Id = "user.recycle-bin",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Recycle Bin",
                ["fa"] = "سبد بازیافت"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Deleted files still recoverable. Emptying it frees the space permanently.",
                ["fa"] = "فایل‌های حذف‌شده که هنوز قابل بازیابی‌اند. خالی کردن آن فضا را به‌طور دائمی آزاد می‌کند."
            },
            Patterns = [new RulePattern("$Recycle.Bin")],
            Category = StorageCategory.TemporaryFiles,
            RiskLevel = RiskLevel.Medium,
            CanDelete = false,
            CanMove = false,
            CanRegenerate = false,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            OfficialMigrationHint = "Empty it from File Explorer when you are sure nothing there is needed.",
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "Emptying the Recycle Bin cannot be undone.",
                ["fa"] = "خالی کردن سبد بازیافت قابل بازگشت نیست."
            },
            Confidence = 0.95
        };

        yield return new StorageRule
        {
            Id = "archive.files",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Archives",
                ["fa"] = "آرشیوها"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Compressed archives. Often left over after extraction, but may be the only copy of something.",
                ["fa"] = "آرشیوهای فشرده. اغلب پس از استخراج باقی می‌مانند، اما ممکن است تنها نسخه‌ی موجود از چیزی باشند."
            },
            Patterns =
            [
                new RulePattern(".zip", RuleMatchTarget.Name, ItemType.File),
                new RulePattern(".7z", RuleMatchTarget.Name, ItemType.File),
                new RulePattern(".rar", RuleMatchTarget.Name, ItemType.File),
                new RulePattern(".iso", RuleMatchTarget.Name, ItemType.File)
            ],
            Category = StorageCategory.Archives,
            RiskLevel = RiskLevel.Medium,
            CanDelete = false,
            CanMove = true,
            CanRegenerate = false,
            Confidence = 0.8
        };

        yield return new StorageRule
        {
            Id = "vm.disk-images",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Virtual machine disks",
                ["fa"] = "دیسک‌های ماشین مجازی"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Virtual hard disks for Hyper-V, VirtualBox or VMware. Each holds a complete guest system.",
                ["fa"] = "دیسک‌های سخت مجازی برای Hyper-V، VirtualBox یا VMware. هر کدام یک سیستم مهمان کامل را در خود دارد."
            },
            Patterns =
            [
                new RulePattern(".vhdx", RuleMatchTarget.Name, ItemType.File),
                new RulePattern(".vhd", RuleMatchTarget.Name, ItemType.File),
                new RulePattern(".vdi", RuleMatchTarget.Name, ItemType.File),
                new RulePattern(".vmdk", RuleMatchTarget.Name, ItemType.File)
            ],
            Category = StorageCategory.VirtualMachines,
            RiskLevel = RiskLevel.High,
            CanDelete = false,
            CanMove = true,
            CanRegenerate = false,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            OfficialMigrationHint = "Move the disk through your hypervisor's own settings so its configuration stays valid.",
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "Shut the virtual machine down first. The guest system lives entirely inside this file.",
                ["fa"] = "ابتدا ماشین مجازی را خاموش کنید. کل سیستم مهمان داخل همین فایل قرار دارد."
            },
            Confidence = 0.92
        };
    }
}
