// Загрузка скриншотов Chart Runner: для каждой локализации версии — набор APP_IPHONE_67, файлы по порядку.
// Usage: node upload_shots_chartrunner.js <versionLocalizationId> <dir>
const fs=require('fs'),path=require('path'),crypto=require('crypto'),https=require('https');
const { request } = require('./asc');
async function put(op, chunk){ const url=new URL(op.url); const headers={}; for(const h of op.requestHeaders||[]) headers[h.name]=h.value; headers['Content-Length']=chunk.length;
  await new Promise((res,rej)=>{ const q=https.request({hostname:url.hostname,path:url.pathname+url.search,method:op.method,headers},r=>{ r.resume(); r.on('end',()=>(r.statusCode>=200&&r.statusCode<300)?res():rej(new Error('upload HTTP '+r.statusCode))); }); q.on('error',rej); q.write(chunk); q.end(); }); }
(async()=>{
  const [locId, dir]=process.argv.slice(2);
  // набор: найти или создать
  let sets=JSON.parse((await request('GET',`/v1/appStoreVersionLocalizations/${locId}/appScreenshotSets`)).body).data||[];
  let set=sets.find(s=>s.attributes.screenshotDisplayType==='APP_IPHONE_67');
  if(!set){ const r=await request('POST','/v1/appScreenshotSets',{data:{type:'appScreenshotSets',attributes:{screenshotDisplayType:'APP_IPHONE_67'},relationships:{appStoreVersionLocalization:{data:{type:'appStoreVersionLocalizations',id:locId}}}}}); set=JSON.parse(r.body).data; console.log('set created',r.status); }
  const files=fs.readdirSync(dir).filter(f=>f.endsWith('.png')).sort();
  for(const f of files){
    const bytes=fs.readFileSync(path.join(dir,f));
    const rs=await request('POST','/v1/appScreenshots',{data:{type:'appScreenshots',attributes:{fileName:f,fileSize:bytes.length},relationships:{appScreenshotSet:{data:{type:'appScreenshotSets',id:set.id}}}}});
    if(rs.status!==201){ console.log('reserve failed',f,rs.status,rs.body.slice(0,300)); continue; }
    const created=JSON.parse(rs.body).data;
    for(const op of created.attributes.uploadOperations) await put(op, bytes.subarray(op.offset, op.offset+op.length));
    const md5=crypto.createHash('md5').update(bytes).digest('hex');
    const c=await request('PATCH',`/v1/appScreenshots/${created.id}`,{data:{type:'appScreenshots',id:created.id,attributes:{uploaded:true,sourceFileChecksum:md5}}});
    console.log(f, 'commit', c.status);
  }
})();
