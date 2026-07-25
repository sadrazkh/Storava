import type { Locale } from '@/localization/messages';
import type { AdvisorDisposition } from '@/models/advisor';

const en = {
  reviewFilter: 'Recommendation filter',
  allItems: 'All items',
  localSignals: 'Local rule signals',
  aiTargeted: 'AI review targets',
  aiTag: 'AI review',
  localTag: 'Local signal',
  aiMapTitle: 'AI-to-local review map',
  aiMapBody: 'The AI saw aggregate rule counts only. Storava mapped its selected signals to real items locally in this browser.',
  noAiTargets: 'Run the AI advisor to create review-target filters without sharing names or paths.',
  showMatches: 'Show matching items',
  targetCleanup: 'Cleanup candidate',
  targetArchive: 'Archive candidate',
  targetInvestigate: 'Investigate',
  itemAddress: 'Address inside selected folder',
  copyAddress: 'Copy address',
  copiedAddress: 'Relative address copied',
  openFile: 'Open local file',
  openingFile: 'Opening…',
  deleteItem: 'Delete from device',
  deleteUnavailable: 'Real file actions require a native Chromium folder permission. Fallback and imported scans remain read-only.',
  deleteTitle: 'Permanent local deletion',
  deleteWarning: 'This removes the selected item from your device, not just from Storava history. Folders are removed recursively. This cannot be undone by Storava.',
  deletePrompt: 'Type the exact item name to confirm',
  deleteCancel: 'Keep item',
  deleteConfirm: 'Delete permanently',
  deleting: 'Deleting…',
  deleteSuccess: 'Item deleted from the device and local scan metadata updated',
  actionFailed: 'The local file action failed',
  rootProtected: 'The selected scan root cannot be deleted.',
  aiLocalBoundary: 'AI recommends signal classes; only you select and confirm a real item.',
  previewBlocked: 'The browser could not open this local file. You can still copy its relative address.',
  resultCount: 'items in this result',
} as const;

type ExplorerMessageKey = keyof typeof en;

const fa: Record<ExplorerMessageKey, string> = {
  reviewFilter: 'فیلتر پیشنهادها',
  allItems: 'همهٔ موارد',
  localSignals: 'نشانه‌های قواعد محلی',
  aiTargeted: 'هدف‌های بررسی AI',
  aiTag: 'پیشنهاد بررسی AI',
  localTag: 'نشانهٔ محلی',
  aiMapTitle: 'نگاشت پیشنهاد AI به فایل‌های محلی',
  aiMapBody: 'هوش مصنوعی فقط تعداد تجمیعی قواعد را دیده است. استوراوا نشانه‌های منتخب را داخل همین مرورگر به موارد واقعی نگاشت کرده است.',
  noAiTargets: 'مشاور AI را اجرا کنید تا بدون اشتراک نام یا مسیر، فیلترهای بررسی ساخته شوند.',
  showMatches: 'نمایش موارد منطبق',
  targetCleanup: 'نامزد پاک‌سازی',
  targetArchive: 'نامزد بایگانی',
  targetInvestigate: 'نیازمند بررسی',
  itemAddress: 'نشانی داخل پوشهٔ انتخاب‌شده',
  copyAddress: 'کپی نشانی',
  copiedAddress: 'نشانی نسبی کپی شد',
  openFile: 'بازکردن فایل محلی',
  openingFile: 'در حال بازکردن…',
  deleteItem: 'حذف از دستگاه',
  deleteUnavailable: 'عملیات واقعی فایل به مجوز بومی پوشه در مرورگر Chromium نیاز دارد. اسکن fallback و فایل واردشده فقط خواندنی هستند.',
  deleteTitle: 'حذف دائمی محلی',
  deleteWarning: 'این کار مورد انتخاب‌شده را از دستگاهتان حذف می‌کند، نه فقط از تاریخچهٔ استوراوا. پوشه‌ها همراه محتویات حذف می‌شوند و استوراوا امکان بازگردانی ندارد.',
  deletePrompt: 'برای تأیید، نام دقیق مورد را وارد کنید',
  deleteCancel: 'نگه‌داشتن مورد',
  deleteConfirm: 'حذف دائمی',
  deleting: 'در حال حذف…',
  deleteSuccess: 'مورد از دستگاه حذف و فرادادهٔ اسکن محلی به‌روز شد',
  actionFailed: 'عملیات محلی فایل ناموفق بود',
  rootProtected: 'ریشهٔ انتخاب‌شدهٔ اسکن قابل حذف نیست.',
  aiLocalBoundary: 'AI نوع نشانه را پیشنهاد می‌دهد؛ انتخاب و تأیید مورد واقعی فقط با شماست.',
  previewBlocked: 'مرورگر نتوانست این فایل محلی را باز کند؛ همچنان می‌توانید نشانی نسبی آن را کپی کنید.',
  resultCount: 'مورد در این نتیجه',
};

export function getExplorerMessages(locale: Locale): typeof en | Record<ExplorerMessageKey, string> {
  return locale === 'fa-IR' ? fa : en;
}

export function dispositionLabel(
  disposition: AdvisorDisposition,
  copy: typeof en | Record<ExplorerMessageKey, string>,
): string {
  if (disposition === 'cleanup-candidate') return copy.targetCleanup;
  if (disposition === 'archive-candidate') return copy.targetArchive;
  return copy.targetInvestigate;
}
