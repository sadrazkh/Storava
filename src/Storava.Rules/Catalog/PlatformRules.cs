using Storava.Domain.Enums;
using Storava.Rules.Model;

namespace Storava.Rules.Catalog;

/// <summary>Containers, VMs, AI model stores, SDKs, game engines and mobile tooling.</summary>
internal static class PlatformRules
{
    internal static IEnumerable<StorageRule> All()
    {
        yield return new StorageRule
        {
            Id = "docker.data",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Docker Desktop data",
                ["fa"] = "داده‌های Docker Desktop"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Images, containers, volumes and the Linux VM disk. Grows steadily and often reaches tens of gigabytes.",
                ["fa"] = "ایمیج‌ها، کانتینرها، والیوم‌ها و دیسک ماشین مجازی لینوکس. پیوسته رشد می‌کند و اغلب به ده‌ها گیگابایت می‌رسد."
            },
            Patterns =
            [
                new RulePattern(@"Docker\wsl", RuleMatchTarget.PathSuffix),
                new RulePattern("DockerDesktopWSL"),
                new RulePattern(@"Docker Desktop\vm-data", RuleMatchTarget.PathSuffix)
            ],
            Category = StorageCategory.Docker,
            Technology = "Docker",
            RiskLevel = RiskLevel.Medium,
            CanDelete = false,
            CanMove = true,
            CanRegenerate = true,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            OfficialMigrationHint = "Docker Desktop → Settings → Resources → Advanced → \"Disk image location\". Reclaim space with 'docker system prune'.",
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "Stop Docker Desktop before moving. Pruning removes unused images, containers and build cache.",
                ["fa"] = "پیش از انتقال، Docker Desktop را متوقف کنید. دستور prune ایمیج‌ها، کانتینرها و کش بیلد بی‌استفاده را حذف می‌کند."
            },
            Confidence = 0.94
        };

        yield return new StorageRule
        {
            Id = "wsl.distribution",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "WSL distribution disk",
                ["fa"] = "دیسک توزیع WSL"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "The virtual disk of a Windows Subsystem for Linux distribution, holding its entire file system.",
                ["fa"] = "دیسک مجازی یک توزیع WSL که کل سیستم فایل آن را در خود دارد."
            },
            Patterns =
            [
                new RulePattern(@"Packages\CanonicalGroupLimited", RuleMatchTarget.PathContains),
                new RulePattern("ext4.vhdx", RuleMatchTarget.Name, ItemType.File),
                new RulePattern(@"lxss", RuleMatchTarget.PathContains)
            ],
            Category = StorageCategory.Wsl,
            Technology = "WSL",
            RiskLevel = RiskLevel.High,
            CanDelete = false,
            CanMove = true,
            CanRegenerate = false,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            OfficialMigrationHint = "Use 'wsl --export' then 'wsl --import' at the new location. Compact with 'wsl --manage <distro> --set-sparse true'.",
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "Contains all files inside the distribution. Shut down WSL ('wsl --shutdown') before touching it.",
                ["fa"] = "همه‌ی فایل‌های داخل توزیع را در خود دارد. پیش از هر کاری WSL را با «wsl --shutdown» خاموش کنید."
            },
            Confidence = 0.9
        };

        yield return new StorageRule
        {
            Id = "huggingface.cache",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Hugging Face model cache",
                ["fa"] = "کش مدل‌های Hugging Face"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Downloaded transformer models and datasets. Individual models are often several gigabytes.",
                ["fa"] = "مدل‌ها و دیتاست‌های دانلودشده. هر مدل معمولاً چند گیگابایت است."
            },
            Patterns =
            [
                new RulePattern(@".cache\huggingface", RuleMatchTarget.PathSuffix),
                new RulePattern("huggingface")
            ],
            Category = StorageCategory.AiModels,
            Technology = "Hugging Face",
            RiskLevel = RiskLevel.Low,
            CanDelete = true,
            CanMove = true,
            CanRegenerate = true,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            FallbackMigrationMethod = MigrationMethod.Junction,
            OfficialMigrationHint = "Set the HF_HOME (or HUGGINGFACE_HUB_CACHE) environment variable to the new location.",
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "Re-downloading large models can take a long time and a lot of bandwidth.",
                ["fa"] = "دانلود مجدد مدل‌های بزرگ زمان و پهنای باند زیادی می‌برد."
            },
            Confidence = 0.95
        };

        yield return new StorageRule
        {
            Id = "ollama.models",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Ollama models",
                ["fa"] = "مدل‌های Ollama"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Local large language models pulled with Ollama. Each model is typically 2–40 GB.",
                ["fa"] = "مدل‌های زبانی بزرگ محلی که با Ollama دریافت شده‌اند. هر مدل معمولاً بین ۲ تا ۴۰ گیگابایت است."
            },
            Patterns = [new RulePattern(@".ollama\models", RuleMatchTarget.PathSuffix)],
            Category = StorageCategory.AiModels,
            Technology = "Ollama",
            RiskLevel = RiskLevel.Low,
            CanDelete = true,
            CanMove = true,
            CanRegenerate = true,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            FallbackMigrationMethod = MigrationMethod.Junction,
            OfficialMigrationHint = "Set the OLLAMA_MODELS environment variable, then restart the Ollama service.",
            Confidence = 0.96
        };

        yield return new StorageRule
        {
            Id = "torch.cache",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "PyTorch model cache",
                ["fa"] = "کش مدل‌های PyTorch"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Pretrained weights downloaded by torch.hub and torchvision.",
                ["fa"] = "وزن‌های پیش‌آموزش‌دیده که torch.hub و torchvision دانلود کرده‌اند."
            },
            Patterns =
            [
                new RulePattern(@".cache\torch", RuleMatchTarget.PathSuffix),
                new RulePattern(@"torch\hub", RuleMatchTarget.PathSuffix)
            ],
            Category = StorageCategory.AiModels,
            Technology = "PyTorch",
            RiskLevel = RiskLevel.Low,
            CanDelete = true,
            CanMove = true,
            CanRegenerate = true,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            FallbackMigrationMethod = MigrationMethod.Junction,
            OfficialMigrationHint = "Set the TORCH_HOME environment variable to the new location.",
            Confidence = 0.92
        };

        yield return new StorageRule
        {
            Id = "stablediffusion.models",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Stable Diffusion models",
                ["fa"] = "مدل‌های Stable Diffusion"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Checkpoints, LoRAs and VAEs for image generation. Checkpoints are commonly 2–7 GB each.",
                ["fa"] = "چک‌پوینت‌ها، LoRAها و VAEها برای تولید تصویر. هر چک‌پوینت معمولاً ۲ تا ۷ گیگابایت است."
            },
            Patterns =
            [
                new RulePattern(@"models\Stable-diffusion", RuleMatchTarget.PathSuffix),
                new RulePattern(@"ComfyUI\models", RuleMatchTarget.PathSuffix)
            ],
            Category = StorageCategory.AiModels,
            Technology = "Stable Diffusion",
            RiskLevel = RiskLevel.Medium,
            CanDelete = false,
            CanMove = true,
            CanRegenerate = false,
            FallbackMigrationMethod = MigrationMethod.Junction,
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "Custom or fine-tuned models may not be downloadable again.",
                ["fa"] = "مدل‌های سفارشی یا fine-tune شده ممکن است دیگر قابل دانلود نباشند."
            },
            Confidence = 0.88
        };

        yield return new StorageRule
        {
            Id = "pip.cache",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "pip download cache",
                ["fa"] = "کش دانلود pip"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Wheels and source archives pip cached for reuse across virtual environments.",
                ["fa"] = "فایل‌های wheel و آرشیوهای سورس که pip برای بازاستفاده در محیط‌های مجازی ذخیره کرده است."
            },
            Patterns = [new RulePattern(@"pip\Cache", RuleMatchTarget.PathSuffix)],
            Category = StorageCategory.PackageCaches,
            Technology = "pip",
            RiskLevel = RiskLevel.Low,
            CanDelete = true,
            CanMove = true,
            CanRegenerate = true,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            FallbackMigrationMethod = MigrationMethod.Junction,
            OfficialMigrationHint = "Set PIP_CACHE_DIR, or clear it with 'pip cache purge'.",
            Confidence = 0.95
        };

        yield return new StorageRule
        {
            Id = "conda.packages",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Conda package cache",
                ["fa"] = "کش پکیج‌های Conda"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Downloaded conda packages shared between environments.",
                ["fa"] = "پکیج‌های دانلودشده‌ی conda که بین محیط‌ها مشترک هستند."
            },
            Patterns = [new RulePattern("pkgs", RuleMatchTarget.Name)],
            Category = StorageCategory.PackageCaches,
            Technology = "Conda",
            RiskLevel = RiskLevel.Medium,
            CanDelete = true,
            CanMove = false,
            CanRegenerate = true,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            OfficialMigrationHint = "Clear with 'conda clean --all'.",
            Confidence = 0.55
        };

        yield return new StorageRule
        {
            Id = "android.sdk",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Android SDK",
                ["fa"] = "اندروید SDK"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Platform images, build tools and NDK versions. Old platform versions are usually the bulk of it.",
                ["fa"] = "ایمیج‌های پلتفرم، ابزارهای بیلد و نسخه‌های NDK. نسخه‌های قدیمی پلتفرم معمولاً بخش عمده‌ی حجم هستند."
            },
            Patterns =
            [
                new RulePattern(@"Android\Sdk", RuleMatchTarget.PathSuffix),
                new RulePattern("android-sdk")
            ],
            Category = StorageCategory.Sdks,
            Technology = "Android",
            RiskLevel = RiskLevel.Medium,
            CanDelete = false,
            CanMove = true,
            CanRegenerate = true,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            OfficialMigrationHint = "Android Studio → SDK Manager → change \"Android SDK Location\", or set ANDROID_HOME.",
            Confidence = 0.93
        };

        yield return new StorageRule
        {
            Id = "android.avd",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Android emulator images",
                ["fa"] = "ایمیج‌های امولاتور اندروید"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Virtual device disk images. Each configured emulator can occupy several gigabytes.",
                ["fa"] = "ایمیج‌های دیسک دستگاه مجازی. هر امولاتور تنظیم‌شده می‌تواند چند گیگابایت اشغال کند."
            },
            Patterns = [new RulePattern(@".android\avd", RuleMatchTarget.PathSuffix)],
            Category = StorageCategory.VirtualMachines,
            Technology = "Android",
            RiskLevel = RiskLevel.Low,
            CanDelete = true,
            CanMove = true,
            CanRegenerate = true,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            FallbackMigrationMethod = MigrationMethod.Junction,
            OfficialMigrationHint = "Set the ANDROID_AVD_HOME environment variable, or delete unused devices in Device Manager.",
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "Emulator state and installed test apps are lost when an image is deleted.",
                ["fa"] = "وضعیت امولاتور و اپ‌های تست نصب‌شده با حذف ایمیج از دست می‌رود."
            },
            Confidence = 0.94
        };

        yield return new StorageRule
        {
            Id = "unity.library",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Unity Library cache",
                ["fa"] = "کش Library یونیتی"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Imported asset artifacts for a Unity project. Regenerated on next open, though re-importing a large project is slow.",
                ["fa"] = "آرتیفکت‌های ایمپورت‌شده‌ی یک پروژه‌ی یونیتی. در بازکردن بعدی بازسازی می‌شود، هرچند ایمپورت مجدد پروژه‌ی بزرگ کند است."
            },
            Patterns = [new RulePattern("Library")],
            Category = StorageCategory.BuildArtifacts,
            Technology = "Unity",
            RiskLevel = RiskLevel.Medium,
            CanDelete = true,
            CanMove = false,
            CanRegenerate = true,
            Confidence = 0.5
        };

        yield return new StorageRule
        {
            Id = "unreal.ddc",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Unreal Derived Data Cache",
                ["fa"] = "کش داده‌ی مشتق‌شده‌ی Unreal"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Compiled shaders and cooked assets. Purely derived data that Unreal rebuilds when needed.",
                ["fa"] = "شیدرهای کامپایل‌شده و اسست‌های cook شده. داده‌ی کاملاً مشتق‌شده که Unreal در صورت نیاز بازسازی می‌کند."
            },
            Patterns =
            [
                new RulePattern("DerivedDataCache"),
                new RulePattern(@"UnrealEngine\Common\DerivedDataCache", RuleMatchTarget.PathSuffix)
            ],
            Category = StorageCategory.BuildArtifacts,
            Technology = "Unreal Engine",
            RiskLevel = RiskLevel.Low,
            CanDelete = true,
            CanMove = true,
            CanRegenerate = true,
            FallbackMigrationMethod = MigrationMethod.Junction,
            Warnings = new Dictionary<string, string>
            {
                ["en"] = "The next build recompiles shaders, which can take a long time.",
                ["fa"] = "بیلد بعدی شیدرها را دوباره کامپایل می‌کند که می‌تواند زمان‌بر باشد."
            },
            Confidence = 0.95
        };

        yield return new StorageRule
        {
            Id = "games.steam-library",
            Titles = new Dictionary<string, string>
            {
                ["en"] = "Steam game library",
                ["fa"] = "کتابخانه‌ی بازی‌های Steam"
            },
            Descriptions = new Dictionary<string, string>
            {
                ["en"] = "Installed Steam games. Steam can move these between drives without any manual work.",
                ["fa"] = "بازی‌های نصب‌شده‌ی Steam. خود Steam می‌تواند این‌ها را بدون کار دستی بین درایوها منتقل کند."
            },
            Patterns = [new RulePattern("steamapps")],
            Category = StorageCategory.GameLibraries,
            Technology = "Steam",
            RiskLevel = RiskLevel.Low,
            CanDelete = false,
            CanMove = true,
            CanRegenerate = true,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            OfficialMigrationHint = "Steam → Settings → Storage: add a library folder on another drive and move games there.",
            Confidence = 0.95
        };
    }
}
