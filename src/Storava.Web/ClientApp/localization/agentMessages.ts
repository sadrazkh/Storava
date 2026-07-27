import type { Locale } from '@/localization/messages';

const en = {
  railLabel: 'Companion Agent',
  kicker: 'LOCAL · PAIRED · NOT THROUGH THE SERVER',
  title: 'The part of your disk a browser cannot see.',
  intro:
    'A browser only ever sees the folder you picked, and only knows a path relative to it. The Agent runs on this computer and talks to this page directly over your own machine — never through the Storava server.',

  signedOutTitle: 'Sign in to use a companion Agent',
  signedOutBody:
    'Pairing ties an Agent to your account so this page can tell yours apart from anything else listening on this machine.',
  signIn: 'Sign in',

  noDevicesTitle: 'No Agent is paired yet',
  noDevicesBody:
    'Generate a pairing code on your account page, then run “storava-agent pair” on the computer you want to connect.',
  openAccount: 'Open account page',

  deviceLabel: 'Paired computer',
  lastSeen: 'Last asked for',
  connect: 'Connect',
  reconnect: 'Try again',
  connecting: 'Looking for the Agent on this machine…',

  connectedTitle: 'Connected',
  connectedBody: 'This page is talking to {name} on 127.0.0.1:{port}. Nothing has been read yet.',
  agentVersion: 'Agent version',
  runningSince: 'Running since',

  permissionTitle: 'Your browser is asking before it reaches this machine',
  permissionBody:
    'Chrome and other Chromium browsers now ask permission before a site may connect to anything on your local network, including this computer. Allow it to let this page reach your Agent. Nothing is sent anywhere — the connection does not leave this machine.',

  notRunningTitle: 'No Agent answered',
  notRunningBody: 'Run “storava-agent serve” on this computer, then try again.',

  otherDeviceTitle: 'That Agent belongs to another computer',
  otherDeviceBody:
    'An Agent is running here, but it is paired as a different device. Open the account page to see which computers are connected.',

  rejectedTitle: 'The Agent refused this page',
  rejectedBody:
    'Its pass was not accepted, which usually means the device was removed from your account. Pair the computer again.',

  noTokenTitle: 'Your account would not issue a pass',
  noTokenBody: 'The device may have been removed. Refresh the list and try again.',

  incompatibleTitle: 'That Agent speaks a different version',
  incompatibleBody: 'Update the Agent on this computer so it matches this page.',

  boundary:
    'The server issues a short-lived pass and learns that you asked. It never sees a drive, a path, or a scan.',

  drivesTitle: 'Drives on this computer',
  drivesBody: 'A browser cannot list these. Choose one to walk, or type any folder path.',
  driveFree: 'free of',
  folderLabel: 'Folder to walk',
  folderPlaceholder: 'C:\\Users\\you\\projects',
  deepMode: 'Deep — also read each file’s size on disk (slower)',
  startScan: 'Scan with the Agent',
  cancelScan: 'Stop',

  scanning: 'Walking {path}',
  scanStats: '{files} files · {folders} folders · {bytes}',
  scanElapsed: '{seconds}s elapsed',
  scanErrors: '{errors} unreadable',

  scanDoneTitle: 'Finished',
  scanCancelledTitle: 'Stopped',
  scanFailedTitle: 'The walk failed',
  scanDoneBody: '{files} files and {folders} folders, {bytes} in total.',

  resultsTitle: 'Largest items',
  resultsBody:
    'These are real paths on this computer — the thing the browser edition can never show you. They stay between the Agent and this page.',
  colPath: 'Path',
  colSize: 'Size',
  colKind: 'Identified as',
  foldersOnly: 'Folders only',
  copyPath: 'Copy',
  copied: 'Copied',
  protectedItem: 'protected',
  noResults: 'Nothing was stored for that walk.',

  notYet: 'Reading only. Nothing changes on this computer until you confirm a specific folder by name.',

  actDelete: 'Delete…',
  actMove: 'Move…',
  actNotPermitted: 'The local rules do not offer an action for this item.',

  confirmTitle: 'Confirm before anything is touched',
  confirmMeasured: 'Measured now: {bytes}. This is what the folder holds at this moment, not what the scan recorded.',
  confirmDeleteBody:
    'This folder goes to the Recycle Bin. Storava has no way to delete anything permanently — not even a copy it made itself — so you can still get it back.',
  confirmMoveBody:
    'The folder is copied first and checked against the original. Only once the copy matches does the original go to the Recycle Bin, so at no point does your data exist in neither place.',
  confirmDestination: 'Move it to',
  confirmDestinationHint: 'Must be on a different drive, or nothing is freed.',
  confirmTypePrompt: 'Type {name} to confirm',
  confirmAction: 'Do it',
  confirmCancel: 'Cancel',

  warnGrew: 'This folder is larger now than when it was scanned.',
  warnShrank: 'This folder is smaller now than when it was scanned.',
  warnHighRisk: 'This item is marked high risk. Make sure nothing is using it right now.',
  warnJunction: 'A link will be left at the old location so tools that hard-code it keep working.',

  actionDoneTitle: 'Done',
  actionDoneDelete: 'Sent to the Recycle Bin, freeing {bytes}. You can restore it from there.',
  actionDoneMove: 'Moved, freeing {bytes}. The original went to the Recycle Bin.',
  actionFailedTitle: 'Nothing was changed',
  resultsStale:
    'These figures come from the earlier walk and no longer match the disk. Scan again for current numbers.',
};

export type AgentMessageKey = keyof typeof en;

const fa: Record<AgentMessageKey, string> = {
  railLabel: 'Agent همراه',
  kicker: 'محلی · متصل · بدون عبور از سرور',
  title: 'بخشی از دیسک شما که مرورگر نمی‌بیند.',
  intro:
    'مرورگر فقط پوشه‌ای را می‌بیند که خودتان انتخاب کرده‌اید و فقط مسیر نسبی به آن را می‌داند. Agent روی همین کامپیوتر اجرا می‌شود و مستقیم با این صفحه حرف می‌زند — نه از طریق سرور Storava.',

  signedOutTitle: 'برای استفاده از Agent همراه وارد شوید',
  signedOutBody:
    'اتصال، Agent را به حساب شما گره می‌زند تا این صفحه بتواند Agent شما را از هر چیز دیگری که روی این دستگاه گوش می‌دهد تشخیص دهد.',
  signIn: 'ورود',

  noDevicesTitle: 'هنوز Agentی متصل نشده است',
  noDevicesBody:
    'در صفحهٔ حساب یک کد اتصال بسازید، بعد روی کامپیوتری که می‌خواهید وصل شود «storava-agent pair» را اجرا کنید.',
  openAccount: 'باز کردن صفحهٔ حساب',

  deviceLabel: 'کامپیوتر متصل',
  lastSeen: 'آخرین درخواست',
  connect: 'اتصال',
  reconnect: 'تلاش دوباره',
  connecting: 'در حال گشتن دنبال Agent روی این دستگاه…',

  connectedTitle: 'متصل شد',
  connectedBody: 'این صفحه با {name} روی ‎127.0.0.1:{port}‎ در ارتباط است. هنوز چیزی خوانده نشده است.',
  agentVersion: 'نسخهٔ Agent',
  runningSince: 'در حال اجرا از',

  permissionTitle: 'مرورگر شما پیش از رسیدن به این دستگاه اجازه می‌خواهد',
  permissionBody:
    'کروم و مرورگرهای مبتنی بر Chromium حالا پیش از آنکه یک سایت بتواند به چیزی در شبکهٔ محلی شما — از جمله همین کامپیوتر — وصل شود، اجازه می‌گیرند. برای اینکه این صفحه به Agent شما برسد اجازه بدهید. چیزی به جایی فرستاده نمی‌شود؛ این ارتباط از همین دستگاه بیرون نمی‌رود.',

  notRunningTitle: 'هیچ Agentی پاسخ نداد',
  notRunningBody: 'روی این کامپیوتر «storava-agent serve» را اجرا کنید و دوباره تلاش کنید.',

  otherDeviceTitle: 'آن Agent مربوط به کامپیوتر دیگری است',
  otherDeviceBody:
    'یک Agent اینجا در حال اجراست، ولی به‌عنوان دستگاه دیگری متصل شده است. برای دیدن کامپیوترهای متصل، صفحهٔ حساب را باز کنید.',

  rejectedTitle: 'Agent این صفحه را نپذیرفت',
  rejectedBody:
    'مجوز آن پذیرفته نشد، که معمولاً یعنی دستگاه از حساب شما حذف شده است. کامپیوتر را دوباره وصل کنید.',

  noTokenTitle: 'حساب شما مجوزی صادر نکرد',
  noTokenBody: 'ممکن است دستگاه حذف شده باشد. فهرست را تازه کنید و دوباره تلاش کنید.',

  incompatibleTitle: 'آن Agent نسخهٔ دیگری صحبت می‌کند',
  incompatibleBody: 'Agent این کامپیوتر را به‌روز کنید تا با این صفحه هماهنگ شود.',

  boundary:
    'سرور فقط یک مجوز کوتاه‌عمر صادر می‌کند و می‌داند که شما درخواست داده‌اید. هیچ درایو، مسیر یا اسکنی نمی‌بیند.',

  drivesTitle: 'درایوهای این کامپیوتر',
  drivesBody: 'مرورگر نمی‌تواند این‌ها را فهرست کند. یکی را برای پیمایش انتخاب کنید، یا مسیر هر پوشه‌ای را بنویسید.',
  driveFree: 'آزاد از',
  folderLabel: 'پوشه‌ای که پیمایش شود',
  folderPlaceholder: 'C:\\Users\\you\\projects',
  deepMode: 'عمیق — حجم روی دیسک هر فایل هم خوانده شود (کندتر)',
  startScan: 'اسکن با Agent',
  cancelScan: 'توقف',

  scanning: 'در حال پیمایش {path}',
  scanStats: '{files} فایل · {folders} پوشه · {bytes}',
  scanElapsed: '{seconds} ثانیه',
  scanErrors: '{errors} غیرقابل‌خواندن',

  scanDoneTitle: 'تمام شد',
  scanCancelledTitle: 'متوقف شد',
  scanFailedTitle: 'پیمایش ناموفق بود',
  scanDoneBody: '{files} فایل و {folders} پوشه، در مجموع {bytes}.',

  resultsTitle: 'بزرگ‌ترین موردها',
  resultsBody:
    'این‌ها مسیرهای واقعی روی این کامپیوتر هستند — همان چیزی که نسخهٔ مرورگری هرگز نمی‌تواند نشانتان بدهد. بین Agent و این صفحه می‌مانند.',
  colPath: 'مسیر',
  colSize: 'حجم',
  colKind: 'شناسایی‌شده به‌عنوان',
  foldersOnly: 'فقط پوشه‌ها',
  copyPath: 'کپی',
  copied: 'کپی شد',
  protectedItem: 'محافظت‌شده',
  noResults: 'برای آن پیمایش چیزی ذخیره نشد.',

  notYet: 'فقط خواندن. تا وقتی یک پوشهٔ مشخص را با نامش تأیید نکنید، هیچ‌چیز روی این کامپیوتر تغییر نمی‌کند.',

  actDelete: 'حذف…',
  actMove: 'جابه‌جایی…',
  actNotPermitted: 'قواعد محلی برای این مورد اقدامی پیشنهاد نمی‌کنند.',

  confirmTitle: 'تأیید کنید تا چیزی دست بخورد',
  confirmMeasured: 'اندازه‌گیری همین حالا: {bytes}. این چیزی است که پوشه در این لحظه دارد، نه آنچه اسکن ثبت کرده بود.',
  confirmDeleteBody:
    'این پوشه به سطل بازیافت می‌رود. Storava هیچ راهی برای حذف دائمی ندارد — حتی برای نسخه‌ای که خودش ساخته — پس همچنان می‌توانید برش گردانید.',
  confirmMoveBody:
    'اول از پوشه کپی گرفته می‌شود و با اصل مقایسه می‌شود. تنها وقتی کپی مطابق بود، اصل به سطل بازیافت می‌رود؛ پس در هیچ لحظه‌ای داده‌های شما در هیچ‌کدام نیستند.',
  confirmDestination: 'جابه‌جایی به',
  confirmDestinationHint: 'باید روی درایو دیگری باشد، وگرنه چیزی آزاد نمی‌شود.',
  confirmTypePrompt: 'برای تأیید {name} را بنویسید',
  confirmAction: 'انجام بده',
  confirmCancel: 'انصراف',

  warnGrew: 'این پوشه اکنون از زمان اسکن بزرگ‌تر شده است.',
  warnShrank: 'این پوشه اکنون از زمان اسکن کوچک‌تر شده است.',
  warnHighRisk: 'این مورد پرخطر علامت خورده است. مطمئن شوید همین حالا چیزی از آن استفاده نمی‌کند.',
  warnJunction: 'یک لینک در مسیر قبلی می‌ماند تا ابزارهایی که آن مسیر را ثابت نوشته‌اند کار کنند.',

  actionDoneTitle: 'انجام شد',
  actionDoneDelete: 'به سطل بازیافت فرستاده شد و {bytes} آزاد شد. از همان‌جا می‌توانید بازگردانیدش.',
  actionDoneMove: 'جابه‌جا شد و {bytes} آزاد شد. نسخهٔ اصلی به سطل بازیافت رفت.',
  actionFailedTitle: 'هیچ‌چیز تغییر نکرد',
  resultsStale:
    'این اعداد از پیمایش قبلی هستند و دیگر با دیسک نمی‌خوانند. برای اعداد فعلی دوباره اسکن کنید.',
};

export function getAgentMessages(locale: Locale): Record<AgentMessageKey, string> {
  return locale === 'fa-IR' ? fa : en;
}
