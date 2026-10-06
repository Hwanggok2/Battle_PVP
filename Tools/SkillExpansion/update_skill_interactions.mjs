import fs from 'node:fs/promises';
import path from 'node:path';
import { FileBlob, SpreadsheetFile } from '@oai/artifact-tool';
const root = path.resolve(import.meta.dirname, '../..');
const report = path.join(root, 'Reports/SkillInteractions');
await fs.mkdir(report, {recursive:true});
const skill = await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(root,'GameData/GameData_Skill.xlsx')));
if(process.argv.includes('--defense-balance') || process.argv.includes('--defense-preview')) {
 const preview=process.argv.includes('--defense-preview');
 const folder=path.join(root,'Reports/WaitingAndDefense'); await fs.mkdir(folder,{recursive:true});
 const strings=await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(root,'GameData/GameData_String.xlsx')));
 const defs=skill.worksheets.getItem('SkillDefinition'), params=skill.worksheets.getItem('SkillParameter'), texts=strings.worksheets.getItem('SkillString');
 const snapshot=wb=>wb.worksheets.items.map(s=>({name:s.name,values:s.getUsedRange().values,formulas:s.getUsedRange().formulas}));
 const expected=[snapshot(skill),snapshot(strings)];
 const find=(s,key,col=0)=>{const row=s.getUsedRange().values.findIndex(r=>r[col]===key)+1;if(!row)throw new Error(key);return row;};
 const edits=[
  ['DEF_Taunt','TauntReadyDurationSeconds,TauntDurationSeconds,CooldownSeconds',
   '{0:0.#}초 안에 적중한 다음 공격이 적을 {1:0.#}초간 도발하며, 받는 피해 감소·반사 강화가 적용됩니다. 대기 중 무기는 주황색. 쿨타임 {2:0.#}초.',
   'Your next hit within {0:0.#}s taunts for {1:0.#}s, reducing incoming damage and increasing reflection. The weapon glows orange while ready. Cooldown: {2:0.#}s.'],
  ['DEF_Bash','StunSeconds,IncomingMultiplier,DurationSeconds,CooldownSeconds',
   '{2:0.#}초 안에 적중한 다음 공격이 적을 {0:0.#}초간 기절시키고, 기절 중 받는 피해를 {1:0.#}배로 만듭니다. 대기 중 무기는 파란색. 쿨타임 {3:0.#}초.',
   'Your next hit within {2:0.#}s stuns for {0:0.#}s and increases damage taken to {1:0.#}x during the stun. The weapon glows blue while ready. Cooldown: {3:0.#}s.']
 ];
 const put=(book,sheet,row,col,value)=>{sheet.getRangeByIndexes(row-1,col,1,1).values=[[value]];expected[book].find(s=>s.name===sheet.name).values[row-1][col]=value;};
 if(!preview) {
  for(const [id,args,ko,en] of edits) {
   const d=find(defs,id,1), t=find(texts,id+'_Desc');
   put(0,defs,d,5,args);
   if(id==='DEF_Bash') {put(0,defs,d,7,10);put(0,defs,d,8,15);}
   put(1,texts,t,1,ko);put(1,texts,t,2,en);put(1,texts,t,3,args.split(',').length);
  }
  for(const [id,key,value] of [['DEF_Taunt','TauntReadyDurationSeconds',10],['DEF_Bash','IncomingMultiplier',1.6]]) {
   const r=params.getUsedRange().values.findIndex(row=>row[0]===id && row[1]===key)+1;if(!r)throw new Error(id+'/'+key);
   put(0,params,r,2,value);
  }
  for(const [index,book,name] of [[0,skill,'Skill'],[1,strings,'String']]) {
   book.recalculate();
   const norm=x=>JSON.stringify(x,(k,v)=>v===undefined||v===''?null:v);
   if(norm(snapshot(book))!==norm(expected[index]))throw new Error('Unexpected changes: '+name);
   console.log((await book.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!',options:{useRegex:true,maxResults:10}})).ndjson);
   await (await SpreadsheetFile.exportXlsx(book)).save(path.join(root,`GameData/GameData_${name}.xlsx`));
  }
  const seedPath=path.join(root,'Tools/SkillExpansion/seed.json'), seed=JSON.parse(await fs.readFile(seedPath,'utf8'));
  for(const [id,args,ko,en] of edits) {
   const row=seed.skills.find(s=>s.id===id);row.args=args;
   if(id==='DEF_Taunt') row.params.TauntReadyDurationSeconds=10;
   else {row.duration=10;row.cooldown=15;row.params.IncomingMultiplier=1.6;}
   const text=seed.strings.find(r=>r[0]===id+'_Desc');text.splice(1,3,ko,en,args.split(',').length);
  }
  await fs.writeFile(seedPath,JSON.stringify(seed,null,2)+'\n');
 }
 for(const [id] of edits) {
  const d=find(defs,id,1), t=find(texts,id+'_Desc');
  console.log(JSON.stringify({id,definition:defs.getRange(`A${d}:J${d}`).values,description:texts.getRange(`A${t}:D${t}`).values}));
  for(const [book,sheet,range,label] of [[skill,'SkillDefinition',`F${d}:I${d}`,'numbers'],[strings,'SkillString',`A${t}:D${t}`,'description']]) {
   const png=await book.render({sheetName:sheet,range,format:'png',scale:1});
   await fs.writeFile(path.join(folder,`${id}-${label}-${preview?'before':'after'}.png`),new Uint8Array(await png.arrayBuffer()));
  }
 }
 process.exit(0);
}
if(process.argv.includes('--charges') || process.argv.includes('--preview-charges')) {
 const folder=path.join(root,'Reports/SkillCharges'); await fs.mkdir(folder,{recursive:true});
 const strings=await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(root,'GameData/GameData_String.xlsx')));
 const defs=skill.worksheets.items.find(s=>s.name==='SkillDefinition'), params=skill.worksheets.items.find(s=>s.name==='SkillParameter');
 const texts=strings.worksheets.items.find(s=>s.name==='SkillString');
 const preview=process.argv.includes('--preview-charges');
 const find=(s,key,column=0)=>{const n=s.getUsedRange().values.findIndex(r=>r[column]===key)+1;if(!n)throw new Error(key);return n;};
 const snapshot=wb=>wb.worksheets.items.map(s=>({name:s.name,values:s.getUsedRange().values,formulas:s.getUsedRange().formulas}));
 const expected=[snapshot(skill),snapshot(strings)];
 const changes=[
  ['TACT_Dash','RollDistance,MaxCharges,CooldownSeconds,RollDurationSeconds','전방으로 {3:0.##}초 동안 {0:0.#}m 대시합니다. 최대 {1:0}회 충전, {2:0.#}초마다 1회 회복.','Dash {0:0.#}m forward in {3:0.##}s. Stores {1:0} charges; recovers one every {2:0.#}s.'],
  ['POLY_Roll','RollDistance,MaxCharges,CooldownSeconds,RollDurationSeconds','전방으로 {3:0.##}초 동안 {0:0.#}m 구릅니다. 최대 {1:0}회 충전, {2:0.#}초마다 1회 회복.','Roll {0:0.#}m forward in {3:0.##}s. Stores {1:0} charges; recovers one every {2:0.#}s.'],
  ['SHARED_Trap','Lifetime,MaxHealthDamageRatio,RootSeconds,CastSeconds,MaxCharges,CooldownSeconds','최대 {4:0}회 충전, {5:0.#}초마다 1회 회복. 스킬 키로 앞을 조준, 클릭으로 {3:0.#}초간 설치. {0:0.#}초 유지. 적 최대 체력 {1:0%} 피해·{2:0.#}초 이동/방향 봉쇄.','Stores {4:0} charges; recovers one every {5:0.#}s. Skill key previews; click to place in {3:0.#}s. Lasts {0:0.#}s; deals {1:0%} max HP and locks movement/turning for {2:0.#}s.']
 ];
 const put=(bookIndex,sheet,row,col,value)=>{
  sheet.getRange(`${String.fromCharCode(65+col)}${row}`).values=[[value]];
  const ref=expected[bookIndex].find(s=>s.name===sheet.name);
  while(ref.values.length<row) { ref.values.push(Array(ref.values[0].length).fill(null)); ref.formulas.push(Array(ref.values[0].length).fill(null)); }
  ref.values[row-1][col]=value;
 };
 if(!preview) {
  for(const [id,args,ko,en] of changes) {
   const d=find(defs,id,1), t=find(texts,id+'_Desc');
   put(0,defs,d,5,args); if(id==='SHARED_Trap')put(0,defs,d,6,1.1);
   put(1,texts,t,1,ko); put(1,texts,t,2,en); put(1,texts,t,3,args.split(',').length);
   const list=[['MaxCharges',id==='SHARED_Trap'?3:2]];
   if(id!=='SHARED_Trap')list.push(['RollDistance',3.6]);
   for(const [key,value] of list) {
    let row=params.getUsedRange().values.findIndex(r=>r[0]===id && r[1]===key)+1;
    if(!row) {row=params.getUsedRange().values.length+1; params.getRange(`A${row}:D${row}`).copyFrom(params.getRange(`A${row-1}:D${row-1}`),'all');}
    [id,key,value,'2026-10-05 충전 및 이동 조정'].forEach((v,c)=>put(0,params,row,c,v));
   }
  }
  for(const [index,book,name] of [[0,skill,'Skill'],[1,strings,'String']]) {
   book.recalculate();
   const actual=snapshot(book);
   // Imported blank cells may be represented by either null or an empty formula.
   const normalized=x=>JSON.stringify(x,(k,v)=>v===undefined||v===''?null:v);
   if(normalized(actual)!==normalized(expected[index]))throw new Error('Unexpected workbook change '+name);
   console.log((await book.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!',options:{useRegex:true,maxResults:10}})).ndjson);
   await (await SpreadsheetFile.exportXlsx(book)).save(path.join(root,`GameData/GameData_${name}.xlsx`));
  }
  const seedPath=path.join(root,'Tools/SkillExpansion/seed.json'); const seed=JSON.parse(await fs.readFile(seedPath,'utf8'));
  for(const [id,args,ko,en] of changes) {
   const row=seed.skills.find(s=>s.id===id); row.args=args; row.params.MaxCharges=id==='SHARED_Trap'?3:2;
   if(id==='SHARED_Trap')row.cast=1.1; else row.params.RollDistance=3.6;
   const text=seed.strings.find(r=>r[0]===id+'_Desc'); text.splice(1,3,ko,en,args.split(',').length);
  }
  await fs.writeFile(seedPath,JSON.stringify(seed,null,2)+'\n');
 }
 for(const [id] of changes) {
  const row=find(texts,id+'_Desc');
  const png=await strings.render({sheetName:'SkillString',range:`A${row}:D${row}`,format:'png',scale:1});
  await fs.writeFile(path.join(folder,`${id}-${preview?'before':'after'}.png`),new Uint8Array(await png.arrayBuffer()));
 }
 console.log(preview?'Rendered original charge descriptions.':'Updated charge data and descriptions.'); process.exit(0);
}
if(process.argv.includes('--skill-state') || process.argv.includes('--preview-state')) {
 const folder=path.join(root,'Reports/SkillStatePolish'); await fs.mkdir(folder,{recursive:true});
 const book=await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(root,'GameData/GameData_String.xlsx')));
 const sheet=book.worksheets.items.find(s=>s.name==='SkillString');
 const preview=process.argv.includes('--preview-state');
 const edits=[
  ['SHARED_Charge_Desc','최대 {0:0.#}초 돌진. 걷기 속도의 {3:0%}로 출발, {1:0.#}초 후 AGI 몰빵 기본 속도의 {4:0%} 도달. 적마다 피해·{2:0.#}초 기절. 적중 후 계속 돌진. 점프·공격·스킬 키로 종료.','Charge up to {0:0.#}s. Start at {3:0%} walk speed; reach {4:0%} base AGI-specialist speed in {1:0.#}s. Damage and stun each enemy once for {2:0.#}s without stopping. Jump, attack or a skill key ends charge.'],
  ['CON_Berserk_Desc','켜짐/꺼짐. 매초 최대 체력 {0:0%} 소모. 부족하면 체력 1을 남기고 종료. 이동 {1:0%}, 공속 {2:0%}, 공격력 {3:0%}. 재생 불가. 종료 후 {4:0.#}초간 쿨타임·재생 절반.','Toggle. Drain {0:0%} max HP/s; stop at 1 HP. Movement {1:0%}, attack speed {2:0%}, attack power {3:0%}. No regeneration. On ending: {4:0.#}s cooldown and half regeneration.']
 ];
 const snapshot=()=>book.worksheets.items.map(s=>({name:s.name,values:s.getUsedRange().values,formulas:s.getUsedRange().formulas}));
 const expected=snapshot();
 for(const [key,ko,en] of edits) {
  const row=sheet.getUsedRange().values.findIndex(r=>r[0]===key)+1;
  if(row<1) throw new Error(key);
  if(!preview) {
   sheet.getRange(`B${row}:C${row}`).values=[[ko,en]];
   expected.find(s=>s.name==='SkillString').values[row-1].splice(1,2,ko,en);
  }
  const rendered=await book.render({sheetName:'SkillString',range:`A${row}:D${row}`,scale:1,format:'png'});
  await fs.writeFile(path.join(folder,`${key}-${preview?'before':'after'}.png`),new Uint8Array(await rendered.arrayBuffer()));
 }
 if(!preview) {
  book.recalculate();
  if(JSON.stringify(snapshot())!==JSON.stringify(expected))throw new Error('Unexpected workbook change');
  console.log((await book.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!',options:{useRegex:true,maxResults:10}})).ndjson);
  await (await SpreadsheetFile.exportXlsx(book)).save(path.join(root,'GameData/GameData_String.xlsx'));
  const seedFile=path.join(root,'Tools/SkillExpansion/seed.json');
  const seed=JSON.parse(await fs.readFile(seedFile,'utf8'));
  let source=await fs.readFile(path.join(root,'Tools/SkillExpansion/seed_data.py'),'utf8');
  for(const [key,ko,en] of edits) {
   const entry=seed.strings.find(r=>r[0]===key); if(!entry) throw new Error('Missing seed '+key);
   source=source.replace(entry[1],ko).replace(entry[2],en); entry[1]=ko; entry[2]=en;
  }
  await fs.writeFile(seedFile,JSON.stringify(seed,null,2)+'\n');
  await fs.writeFile(path.join(root,'Tools/SkillExpansion/seed_data.py'),source);
 }
 console.log(preview?'Rendered current descriptions.':'Updated two descriptions; all other workbook values and formulas preserved.');
 process.exit(0);
}
if(process.argv.includes('--charge-continuity') || process.argv.includes('--preview-charge')) {
 const folder=path.join(root,'Reports/ChargeContinuity'); await fs.mkdir(folder,{recursive:true});
 const strings=await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(root,'GameData/GameData_String.xlsx')));
 const preview=process.argv.includes('--preview-charge');
 const defs=skill.worksheets.items.find(s=>s.name==='SkillDefinition');
 const params=skill.worksheets.items.find(s=>s.name==='SkillParameter');
 const texts=strings.worksheets.items.find(s=>s.name==='SkillString');
 const locate=(s,p)=>{const i=s.getUsedRange().values.findIndex(p); if(i<0)throw new Error('Missing charge row: '+s.name);return i+1;};
 const initial=locate(params,r=>r[0]==='SHARED_Charge'&&r[1]==='InitialSpeedMultiplier');
 const maximum=locate(params,r=>r[0]==='SHARED_Charge'&&r[1]==='MaximumAgiMultiplier');
 const definition=locate(defs,r=>r[1]==='SHARED_Charge'), description=locate(texts,r=>r[0]==='SHARED_Charge_Desc');
 const snapshot=book=>book.worksheets.items.map(s=>({name:s.name,values:s.getUsedRange().values,formulas:s.getUsedRange().formulas}));
 const expectedSkill=snapshot(skill),expectedStrings=snapshot(strings);
 const args='DurationSeconds,AccelerationSeconds,StunSeconds,InitialSpeedMultiplier,MaximumAgiMultiplier';
 const ko='최대 {0:0.#}초 돌진. 걷기 속도의 {3:0%}로 출발해 {1:0.#}초 후 AGI 몰빵 기본 속도의 {4:0%} 도달. 적마다 피해·{2:0.#}초 기절. 적중 후 계속 돌진, 공격 시 종료.';
 const en='Charge up to {0:0.#}s. Start at {3:0%} walk speed; reach {4:0%} base AGI-specialist speed in {1:0.#}s. Damage and stun each enemy once for {2:0.#}s without stopping. Attack to end.';
 if(!preview) {
  params.getRange(`C${initial}`).values=[[.8]]; params.getRange(`C${maximum}`).values=[[1.3]];
  defs.getRange(`F${definition}`).values=[[args]]; texts.getRange(`B${description}:D${description}`).values=[[ko,en,5]];
  expectedSkill.find(s=>s.name==='SkillParameter').values[initial-1][2]=.8;
  expectedSkill.find(s=>s.name==='SkillParameter').values[maximum-1][2]=1.3;
  expectedSkill.find(s=>s.name==='SkillDefinition').values[definition-1][5]=args;
  expectedStrings.find(s=>s.name==='SkillString').values[description-1].splice(1,3,ko,en,5);
  for(const [name,book,expected] of [['Skill',skill,expectedSkill],['String',strings,expectedStrings]]) {
   book.recalculate();
   if(JSON.stringify(snapshot(book))!==JSON.stringify(expected))throw new Error('Unrelated workbook changes: '+name);
   console.log((await book.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!',options:{useRegex:true,maxResults:10}})).ndjson);
   await (await SpreadsheetFile.exportXlsx(book)).save(path.join(root,`GameData/GameData_${name}.xlsx`));
  }
 }
 for(const [book,sheetName,range,label] of [[skill,'SkillParameter',`A${initial-1}:D${maximum+2}`,'speed'],[strings,'SkillString',`A${description}:D${description}`,'description']]) {
  const image=await book.render({sheetName,range,scale:1,format:'png'});
  await fs.writeFile(path.join(folder,`${label}-${preview?'before':'after'}.png`),new Uint8Array(await image.arrayBuffer()));
  console.log((await book.inspect({kind:'table',range:`${sheetName}!${range}`,include:'values',tableMaxRows:6,tableMaxCols:4})).ndjson);
 }
 process.exit(0);
}
if(process.argv.includes('--instant-defense') || process.argv.includes('--preview-defense')) {
 const folder=path.join(root,'Reports/DefenseSkillVfx'); await fs.mkdir(folder,{recursive:true});
 const sheet=skill.worksheets.items.find(s=>s.name==='SkillDefinition');
 const rows=['DEF_Bash','DEF_Thorns'].map(id=>{const i=sheet.getUsedRange().values.findIndex(r=>r[1]===id); if(i<0) throw new Error(id); return i+1;});
 const preview=process.argv.includes('--preview-defense');
 const before=skill.worksheets.items.map(s=>({name:s.name,values:s.getUsedRange().values,formulas:s.getUsedRange().formulas}));
 if(!preview) {
  for(const row of rows) sheet.getRange(`G${row}`).values=[[0]];
  skill.recalculate();
  const after=skill.worksheets.items.map(s=>({name:s.name,values:s.getUsedRange().values,formulas:s.getUsedRange().formulas}));
  for(let i=0;i<before.length;i++) {
   if(before[i].name==='SkillDefinition') for(const row of rows) before[i].values[row-1][6]=0;
   if(JSON.stringify(before[i])!==JSON.stringify(after[i])) throw new Error('Unexpected sheet changes: '+before[i].name);
  }
  console.log((await skill.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!',options:{useRegex:true,maxResults:10}})).ndjson);
  await (await SpreadsheetFile.exportXlsx(skill)).save(path.join(root,'GameData/GameData_Skill.xlsx'));
 }
 const image=await skill.render({sheetName:'SkillDefinition',range:`B${rows[0]}:I${rows[1]}`,scale:1,format:'png'});
 await fs.writeFile(path.join(folder,preview?'data-before.png':'data-after.png'),new Uint8Array(await image.arrayBuffer()));
 console.log((await skill.inspect({kind:'table',range:`SkillDefinition!B${rows[0]}:I${rows[1]}`,include:'values',tableMaxRows:2,tableMaxCols:8})).ndjson);
 process.exit(0);
}
const strings = await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(root,'GameData/GameData_String.xlsx')));
const sheet = (book, name) => book.worksheets.items.find(s=>s.name===name);
const defs = sheet(skill,'SkillDefinition'), params = sheet(skill,'SkillParameter'), texts = sheet(strings,'SkillString');
function find(s, predicate) { const i=s.getUsedRange().values.findIndex(predicate); if(i<0) throw new Error('Missing row in '+s.name); return i+1; }
function param(id,key,value) {
 const rows=params.getUsedRange().values; const found=rows.findIndex(r=>r[0]===id&&r[1]===key);
 const row=found<0?rows.length+1:found+1;
 if(found<0) { params.getRange(`A${row}:D${row}`).copyFrom(params.getRange(`A${row-1}:D${row-1}`),'all'); }
 params.getRange(`A${row}:D${row}`).values=[[id,key,value,'2026-10-04 스킬 조작·연출 개선']];
}
param('STR_Hook','Range',6); param('STR_Hook','ThrowReleaseSeconds',.24); param('AGI_Knife','ThrowReleaseSeconds',.10);
param('STR_Hook','ProjectileSpeed',18); param('STR_Hook','PullSpeed',12);
param('STR_Hook','RetrieveSeconds',.4);
param('STR_Hook','FixedDamage',10);
param('SHARED_Charge','AccelerationSeconds',2.5);
param('AGI_Stealth','LocalAlpha',.5);
param('STR_Lifesteal','AttackSpeedBonus',.2);
param('STR_Lifesteal','MoveBonus',.1);
param('STR_Lifesteal','LifestealRatio',.1);
defs.getRange(`F${find(defs,r=>r[1]==='STR_Lifesteal')}`).values=[['DurationSeconds,AttackSpeedBonus,MoveBonus,LifestealRatio']];
for(const [id,duration] of [['STR_Hook',.85],['AGI_Knife',.55],['SHARED_Trap',2.2]]) defs.getRange(`G${find(defs,r=>r[1]===id)}`).values=[[duration]];
defs.getRange(`H${find(defs,r=>r[1]==='SHARED_Charge')}`).values=[[4]];
defs.getRange(`F${find(defs,r=>r[1]==='STR_Hook')}`).values=[['Range,FixedDamage']];
defs.getRange(`F${find(defs,r=>r[1]==='SHARED_Trap')}`).values=[['Lifetime,MaxHealthDamageRatio,RootSeconds,CastSeconds']];
function text(key,ko,en,args) { const row=find(texts,r=>r[0]===key);texts.getRange(`B${row}:D${row}`).values=[[ko,en,args]]; }
text('STR_Lifesteal_Desc','{0:0.#}초 동안 공격속도 {1:0%}, 이동속도 {2:0%} 상승. 검으로 준 피해의 {3:0%}를 체력으로 회복합니다.','Gain {1:0%} attack speed and {2:0%} movement speed for {0:0.#}s. Heal for {3:0%} of sword damage dealt.',4);
text('DEF_Fortify_Desc','땅에 엎드려 {0:0.#}초간 버팁니다. 이동할 수 없으며 방어력이 기본의 {1:0%}가 됩니다.','Brace against the ground for {0:0.#}s. You cannot move; defense becomes {1:0%} of its base value.',2);
text('STR_Hook_Desc','사거리 {0:0.#}m의 갈고리를 던져 고정 피해 {1:0}을 주고 앞으로 끌어옵니다. 끌려오는 적은 시전자를 바라보며 이동·공격할 수 없습니다. 시전자도 회수까지 이동·공격 불가.','Throw a hook up to {0:0.#}m for {1:0} fixed damage and pull the target toward you. The target faces you and cannot move or attack during the pull. You cannot move or attack until retrieval.',2);
text('AGI_Stealth_Desc','적에게 모습을 숨깁니다. 본인에게는 반투명하게 보입니다. 공격·피격 또는 누적 {0:0.#}m 이동 시 해제. 해제 후 {1:0.#}초간 공격력이 기본의 {2:0%}가 됩니다.','Become invisible to enemies and translucent to yourself. Attack, damage or {0:0.#}m total travel breaks stealth, granting {2:0%} attack power for {1:0.#}s.',3);
text('AGI_Knife_Desc','스킬 키로 오른팔을 당겨 준비, 좌클릭으로 투척합니다. 준비 중 이동·앉기 가능. 최대 {0:0}개, {1:0.#}초마다 1개 충전. 공격력의 {2:0%} 피해와 독 1중첩. 다시 키를 누르면 취소.','Ready with the skill key, then left-click to throw. Walk and crouch while ready. Holds {0:0} knives; one recharges every {1:0.#}s. Deals {2:0%} attack damage and one poison stack. Press the skill key again to cancel.',3);
text('SHARED_Trap_Desc','스킬 키로 바로 앞 설치 위치 확인, 좌클릭으로 {3:0.#}초간 조립. 다시 키를 누르면 취소. 덫은 {0:0.#}초간 유지되며, 적에게 최대 체력의 {1:0%} 피해와 {2:0.#}초간 이동·방향 봉쇄.','Preview a spot just ahead with the skill key; left-click to assemble for {3:0.#}s. Press the key again to cancel. Lasts {0:0.#}s; deals {1:0%} max-health damage and roots movement/turning for {2:0.#}s.',4);
const ui=sheet(strings,'UiString');
for(const row of [['UI_TrainingDps','DPS {0:0.0}','DPS {0:0.0}',1],['UI_TrapPreviewReady','좌클릭 설치 · 스킬 키 취소','Left-click to place · skill key to cancel',0],['UI_TrapPreviewBlocked','설치할 수 없는 위치 · 스킬 키 취소','Cannot place here · skill key to cancel',0]]) {
 const rows=ui.getUsedRange().values; let i=rows.findIndex(r=>r[0]===row[0]); const n=i<0?rows.length+1:i+1;
 if(i<0)ui.getRange(`A${n}:D${n}`).copyFrom(ui.getRange(`A${n-1}:D${n-1}`),'all');
 ui.getRange(`A${n}:D${n}`).values=[row];
}
for(const [name,wb] of [['Skill',skill],['String',strings]]) {
 wb.recalculate();
 const file=await SpreadsheetFile.exportXlsx(wb); await file.save(path.join(root,`GameData/GameData_${name}.xlsx`));
 console.log((await wb.inspect({kind:'match',searchTerm:'#REF!|#VALUE!|#NAME\\?',options:{useRegex:true,maxResults:5},maxChars:700})).ndjson);
}
const hookRow=find(params,r=>r[0]==='STR_Hook'&&r[1]==='Range');
const render=await skill.render({sheetName:'SkillParameter',range:`A${hookRow}:D${hookRow+4}`,scale:1,format:'png'});
await fs.writeFile(path.join(report,'hook-data.png'),new Uint8Array(await render.arrayBuffer()));
console.log('Updated Skill and String workbooks without rebuilding seed data.');
const bloodlust=find(texts,r=>r[0]==='STR_Lifesteal_Desc');
const preview=await strings.render({sheetName:'SkillString',range:`A${bloodlust}:D${bloodlust}`,scale:1,format:'png'});
await fs.mkdir(path.join(root,'Reports/BowSkillPolish'),{recursive:true});
await fs.writeFile(path.join(root,'Reports/BowSkillPolish/bloodlust-data.png'),new Uint8Array(await preview.arrayBuffer()));
console.log((await skill.inspect({kind:'table',range:`SkillDefinition!B${find(defs,r=>r[1]==='STR_Lifesteal')}:I${find(defs,r=>r[1]==='STR_Lifesteal')}`,include:'values',tableMaxRows:1,tableMaxCols:8})).ndjson);
