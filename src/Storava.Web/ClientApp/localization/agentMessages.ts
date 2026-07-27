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
  notYet:
    'Reading drives and scanning through the Agent are not built yet. Connecting proves the channel works and does nothing else.',
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
  notYet:
    'خواندن درایوها و اسکن از طریق Agent هنوز ساخته نشده است. اتصال فقط ثابت می‌کند که کانال کار می‌کند و کار دیگری نمی‌کند.',
};

export function getAgentMessages(locale: Locale): Record<AgentMessageKey, string> {
  return locale === 'fa-IR' ? fa : en;
}
