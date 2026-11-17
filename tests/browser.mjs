import { chromium } from 'playwright';

const origin = process.env.LAB_URL || 'http://127.0.0.1:5088';
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext();
const page = await context.newPage();
const observations = {};
page.on('console', message => console.log('BROWSER:', message.type(), message.text()));
page.on('pageerror', error => console.log('PAGEERROR:', error.message));

async function press(id) {
  const before = Number(await page.locator('#version').textContent());
  await page.locator('#' + id).click();
  await page.waitForFunction(previous =>
    Number(document.querySelector('#version')?.textContent) > previous,
    before, { timeout: 20000 });
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
  observations.emptyContainerBeforeSeed = await press('emptyjar');
  observations.sharedContainerBeforeSeed = await press('sharedjar');
  const seedA = await page.evaluate(async () => await (await fetch('/lab/seed', {method:'POST', credentials:'include'})).json());
  assert(seedA.seeded && seedA.forUser === 'lab-user', 'failed to seed container from authenticated browser request');
  observations.sharedContainerAfterSeed = await press('sharedjar');
  observations.emptyContainerAfterSeed = await press('emptyjar');

  assert(observations.browserBeforeLogout.authenticated, 'browser did not authenticate');
  assert(observations.circuitBeforeLogout.authenticated, 'Blazor circuit not authenticated');
  assert(observations.serverBaseline.authenticated === false, 'baseline unexpectedly forwarded cookie');
  assert(observations.serverInclude.authenticated === false,
    'Include on server HttpClient unexpectedly forwarded cookie; inspect transport');
  assert(!observations.emptyContainerBeforeSeed.authenticated && !observations.sharedContainerBeforeSeed.authenticated,
    'unseeded server CookieContainer unexpectedly authenticated');
  assert(observations.sharedContainerAfterSeed.authenticated && observations.sharedContainerAfterSeed.name === 'lab-user',
    'explicitly seeded CookieContainer did not authenticate');
  assert(!observations.emptyContainerAfterSeed.authenticated,
    'independently created CookieContainer unexpectedly shared cookie');
  assert(observations.serverInclude.credentialOptionObserved === 'Include',
    'DelegatingHandler did not execute');

  // Logout through a second tab: original circuit stays alive.
  const logoutTab = await context.newPage();
  await logoutTab.goto(origin + '/logout');
  observations.browserAfterLogout = await page.evaluate(async () =>
    await (await fetch('/api/whoami', { credentials: 'include' })).json());
  observations.circuitAfterLogout = await press('circuit');
  observations.serverAfterLogout = await press('include');
  observations.sharedContainerAfterLogout = await press('sharedjar');
  observations.emptyContainerAfterLogout = await press('emptyjar');
  const freshTab = await context.newPage();
  await freshTab.goto(origin + '/');
  observations.freshNavigation = await freshTab.evaluate(async () =>
    await (await fetch('/api/whoami', { credentials: 'include' })).json());

  assert(!observations.browserAfterLogout.authenticated, 'browser still authenticated after logout');
  assert(observations.circuitAfterLogout.authenticated,
    'existing circuit unexpectedly revalidated immediately; report the observation');
  assert(!observations.serverAfterLogout.authenticated, 'server Include unexpectedly authenticated');
  assert(!observations.freshNavigation.authenticated, 'new navigation unexpectedly authenticated');
  assert(observations.sharedContainerAfterLogout.authenticated && observations.sharedContainerAfterLogout.name === 'lab-user',
    'server CookieContainer did not preserve prior cookie after browser logout');
  assert(!observations.emptyContainerAfterLogout.authenticated, 'empty container unexpectedly authenticated after logout');

  // A distinct browser session cannot automatically update/clear an existing shared server cookie jar.
  const otherContext = await browser.newContext();
  const otherPage = await otherContext.newPage();
  await otherPage.goto(origin + '/login?user=other-user');
  await otherPage.locator('#ready').getByText('ready').waitFor({timeout: 30000});
  observations.otherBrowser = await otherPage.evaluate(async () =>
    await (await fetch('/api/whoami', {credentials:'include'})).json());
  observations.sharedContainerViewedByOtherUser = await (async () => {
    const before = Number(await otherPage.locator('#version').textContent());
    await otherPage.locator('#sharedjar').click();
    await otherPage.waitForFunction(previous =>
      Number(document.querySelector('#version')?.textContent) > previous,
      before, {timeout:20000});
    return JSON.parse((await otherPage.locator('#result').textContent()).trim());
  })();
  assert(observations.otherBrowser.name === 'other-user', 'second browser identity was not established');
  assert(observations.sharedContainerViewedByOtherUser.name === 'lab-user',
    'shared CookieContainer did not demonstrate cross-user credential reuse');
  await otherContext.close();
  console.log(JSON.stringify({ status: 'PASS', observations }, null, 2));
} catch (error) {
  console.log('OBSERVATIONS_SO_FAR', JSON.stringify(observations));
  console.log('PAGE_BODY', (await page.locator('body').innerText()).slice(0, 4000));
  throw error;
} finally {
  await browser.close();
}
