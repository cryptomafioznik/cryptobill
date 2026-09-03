#!/usr/bin/env node
// Отправка версии Chart Runner на ревью через App Store Connect API.
// Всё остальное (метаданные, скриншоты, билд, цена, территории, приватность) уже заполнено.
// Запуск: node tools/asc-submit.cjs
const { request } = require(require('os').homedir() + '/.appstoreconnect/asc.js');
const APP = '6808117432', VER = 'd4be1bd2-f686-48f8-8941-305aeccc6bfd';
const show = (n, x) => { let e = ''; try { const j = JSON.parse(x.body); if (j.errors) e = j.errors.map(z => z.title + ': ' + (z.detail || '')).join(' | '); } catch {} console.log(n, x.status, e); return x; };
(async () => {
  show('contentRights', await request('PATCH', `/v1/apps/${APP}`, { data: { type: 'apps', id: APP, attributes: { contentRightsDeclaration: 'DOES_NOT_USE_THIRD_PARTY_CONTENT' } } }));
  show('releaseType', await request('PATCH', `/v1/appStoreVersions/${VER}`, { data: { type: 'appStoreVersions', id: VER, attributes: { releaseType: 'AFTER_APPROVAL' } } }));
  const rs = show('reviewSubmission', await request('POST', '/v1/reviewSubmissions', { data: { type: 'reviewSubmissions', attributes: { platform: 'IOS' }, relationships: { app: { data: { type: 'apps', id: APP } } } } }));
  let sub = null; try { sub = JSON.parse(rs.body).data.id; } catch {}
  if (!sub) { const ex = await request('GET', `/v1/apps/${APP}/reviewSubmissions?filter[state]=READY_FOR_REVIEW,WAITING_FOR_REVIEW,IN_REVIEW,UNRESOLVED_ISSUES`); try { sub = JSON.parse(ex.body).data[0].id; console.log('existing submission', sub); } catch {} }
  if (!sub) return;
  show('item', await request('POST', '/v1/reviewSubmissionItems', { data: { type: 'reviewSubmissionItems', relationships: { reviewSubmission: { data: { type: 'reviewSubmissions', id: sub } }, appStoreVersion: { data: { type: 'appStoreVersions', id: VER } } } } }));
  const fin = show('SUBMIT', await request('PATCH', `/v1/reviewSubmissions/${sub}`, { data: { type: 'reviewSubmissions', id: sub, attributes: { submitted: true } } }));
  try { console.log('state:', JSON.parse(fin.body).data.attributes.state); } catch {}
})();
