import fs from 'node:fs/promises';
import path from 'node:path';
import { Workbook, SpreadsheetFile } from '@oai/artifact-tool';
const root=path.resolve(import.meta.dirname,'../..');
const data=JSON.parse(await fs.readFile(path.join(import.meta.dirname,'seed.json'),'utf8'));
await fs.mkdir(path.join(root,'GameData'),{recursive:true});
await fs.mkdir(path.join(root,'Reports/SkillExpansion'),{recursive:true});
const col=i=>String.fromCharCode(65+i);
function sheet(wb,name,fields,types,labels,rows,widths){
 const s=wb.worksheets.add(name); const end=col(fields.length-1); const values=[fields,types,fields.map(()=> 'All'),labels,...rows];
 s.getRange(`A1:${end}${values.length}`).values=values;
 const all=s.getRange(`A1:${end}${values.length}`); all.format.font.name='Malgun Gothic'; all.format.font.size=10; all.format.wrapText=true; all.format.verticalAlignment='center'; all.format.rowHeight=34;
 s.showGridLines=false; s.freezePanes.freezeRows(4); s.freezePanes.freezeColumns(1);
 s.getRange(`A1:${end}1`).format.fill='#173747'; s.getRange(`A1:${end}1`).format.font.color='#FFFFFF'; s.getRange(`A1:${end}1`).format.font.bold=true; s.getRange(`A1:${end}1`).format.rowHeight=34;
 s.getRange(`A2:${end}3`).format.fill='#E7F1F4'; s.getRange(`A2:${end}3`).format.rowHeight=22;
 s.getRange(`A4:${end}4`).format.fill='#D5E5EA'; s.getRange(`A4:${end}4`).format.rowHeight=56;
 for(let i=0;i<fields.length;i++){s.getRange(`${col(i)}1:${col(i)}${values.length}`).format.columnWidth=widths[i]??20; if(types[i]==='float'||types[i]==='int') s.getRange(`${col(i)}5:${col(i)}${values.length}`).setNumberFormat(types[i]==='int'?'0':'0.###');}
 for(let r=5;r<=values.length;r++) if(r%2) s.getRange(`A${r}:${end}${r}`).format.fill='#F1F7F9';
 return s;
}
const outputs=[];
async function save(wb,topic){
 wb.recalculate();
 console.log((await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A',options:{useRegex:true,maxResults:10},maxChars:600})).ndjson);
 for(let i=0;i<wb.worksheets.items.length;i++){
  const s=wb.worksheets.items[i]; const used=s.getUsedRange();
  const preview=await wb.render({sheetName:s.name,range:`A1:${col(used.columnCount-1)}${Math.min(9,used.rowCount)}`,scale:1,format:'png'});
  await fs.writeFile(path.join(root,`Reports/SkillExpansion/${s.name}.png`),new Uint8Array(await preview.arrayBuffer()));
 }
 const out=await SpreadsheetFile.exportXlsx(wb); await out.save(path.join(root,`GameData/GameData_${topic}.xlsx`)); outputs.push(topic);
}
let wb=Workbook.create();
sheet(wb,'SkillDefinition',['ExportType','SkillId','Kind','NameKey','DescriptionKey','DescriptionArgs','CastSeconds','DurationSeconds','CooldownSeconds','Source'],['string','string','int','string','string','string','float','float','float','string'],['USE: 사용 / NONE: 보관','변경 불가 스킬 ID','기존 enum 번호 보존','이름 String 키','설명 String 키','설명에 넣는 수치 키 순서','시전 시간 (초)','지속 시간 (초), 0: 별도 규칙','재사용 대기 (초)','값의 출처와 미지정 수치'],data.skills.map(r=>[r.enabled?'USE':'NONE',r.id,r.kind,r.id+'_Name',r.id+'_Desc',r.args,r.cast,r.duration,r.cooldown,r.source]),[12,25,10,29,29,47,17,21,19,36]);
sheet(wb,'SkillParameter',['SkillId','Key','Value','Notes'],['string','string','float','string'],['SkillDefinition의 스킬 ID','코드에서 읽는 수치 키','비율은 0~1, 배율은 1이 기본','값의 출처. Seconds: 초, Range/Distance: 유닛'],data.skills.filter(r=>r.enabled).flatMap(r=>Object.entries(r.params).map(([k,v])=>[r.id,k,v,r.source])),[27,43,22,52]);
await save(wb,'Skill');
wb=Workbook.create();
sheet(wb,'JobDefinition',['JobIndex','JobId','NameKey','RequirementKey','DescriptionKey','SlotCount'],['int','string','string','string','string','int'],['기존 직업 UI 순서','고정 직업 ID','이름 String 키','직업 조건 String 키','설명 String 키','입장 시 장착 개수'],data.jobs.map((j,i)=>[i,j[0],'JOB_'+j[0]+'_Name','JOB_'+j[0]+'_Requirement','JOB_'+j[0]+'_Desc',2]),[16,16,29,33,29,20]);
sheet(wb,'JobSkillPool',['JobIndex','SkillId','DefaultSlot','SortOrder'],['int','string','int','int'],['JobDefinition의 JobIndex','장착 가능한 스킬 ID','기본 슬롯 0/1, 나머지는 -1','직업 내 표시 순서'],data.jobs.flatMap((j,i)=>j[7].map((kind,n)=>[i,data.skills.find(s=>s.kind===kind).id,n<2?n:-1,n])),[20,30,31,25]);
await save(wb,'Character');
wb=Workbook.create();
for(const [name,rows] of [['SkillString',data.strings.filter(r=>!r[0].startsWith('JOB_')&&!r[0].startsWith('UI_'))],['UiString',data.strings.filter(r=>r[0].startsWith('JOB_')||r[0].startsWith('UI_'))]]){
 const s=sheet(wb,name,['SearchKey','Content_Kor','Content_Eng','FormatArgCount'],['string','string','string','int'],['변경 불가 문자열 키','한국어 표시 문구','영어 표시 문구','{0}부터 연속된 인자 개수'],rows,[35,85,85,20]);
 s.getRange(`A5:D${rows.length+4}`).format.rowHeight=86;
}
await save(wb,'String');
console.log(JSON.stringify({outputs}));
