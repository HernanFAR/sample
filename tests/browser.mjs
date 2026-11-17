import { chromium } from 'playwright';

const origin = process.env.LAB_URL || 'http://127.0.0.1:5088';
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext();
const page = await context.newPage();
const observations = {};
page.on('console', message => console.log('BROWSER:', message.type(), message.text()));
page.on('pageerror', error => console.log('PAGEERROR:', error.message));

async function press(id) {
  const before = await page.locator('#result').textContent();
  await page.locator('#' + id).click();
  await page.waitForFunction((previous) => {
    const value = document.querySelector('#result')?.textContent?.trim();
    return value && value !== 'not-run' && value !== previous;
  }, before?.trim(), { timeout: 20000 });
  const value = await page.locator('#result').textContent();
  return JSON.parse(value.trim());
}

function assert(condition, message) {
  if (!condition) throw Error(message);
}

try {
  await page.goto(origin + '/login');
  await page.locator('#include').waitFor();
  await page.locator('#ready').getByText('ready').waitFor({timeout: 30000});
  observations.browserBeforeLogout = await page.evaluate(async () =>
    await (await fetch('/api/whoami', { credentials: 'include' })).json());
  observations.circuitBeforeLogout = await press('circuit');
  observations.serverBaseline = await press('baseline');
  observations.serverInclude = await press('include');

  assert(observations.browserBeforeLogout.authenticated, 'browser did not authenticate');
  assert(observations.circuitBeforeLogout.authenticated, 'Blazor circuit not authenticated');
  assert(observations.serverBaseline.authenticated === false, 'baseline unexpectedly forwarded cookie');
  assert(observations.serverInclude.authenticated === false,
    'Include on server HttpClient unexpectedly forwarded cookie; inspect transport');
  assert(observations.serverInclude.credentialOptionObserved === 'Include',
    'DelegatingHandler did not execute');

  // Logout through a second tab: original circuit stays alive.
  const logoutTab = await context.newPage();
  await logoutTab.goto(origin + '/logout');
  observations.browserAfterLogout = await page.evaluate(async () =>
    await (await fetch('/api/whoami', { credentials: 'include' })).json());
  observations.circuitAfterLogout = await press('circuit');
  observations.serverAfterLogout = await press('include');
  const freshTab = await context.newPage();
  await freshTab.goto(origin + '/');
  observations.freshNavigation = await freshTab.evaluate(async () =>
    await (await fetch('/api/whoami', { credentials: 'include' })).json());

  assert(!observations.browserAfterLogout.authenticated, 'browser still authenticated after logout');
  assert(observations.circuitAfterLogout.authenticated,
    'existing circuit unexpectedly revalidated immediately; report the observation');
  assert(!observations.serverAfterLogout.authenticated, 'server Include unexpectedly authenticated');
  assert(!observations.freshNavigation.authenticated, 'new navigation unexpectedly authenticated');
  console.log(JSON.stringify({ status: 'PASS', observations }, null, 2));
} catch (error) {
  console.log('OBSERVATIONS_SO_FAR', JSON.stringify(observations));
  console.log('PAGE_BODY', (await page.locator('body').innerText()).slice(0, 4000));
  throw error;
} finally {
  await browser.close();
}
