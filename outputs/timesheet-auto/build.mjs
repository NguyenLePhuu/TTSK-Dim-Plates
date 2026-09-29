import fs from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {FileBlob, SpreadsheetFile} from '@oai/artifact-tool';
const dir = new URL('.', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/,'$1');
const wb=await SpreadsheetFile.importXlsx(await FileBlob.load('C:/Users/phu17/OneDrive/Desktop/TTSKVN 時間管理 - Phú.xlsx'));
const s=wb.worksheets.getItem('2026年09');
if(process.argv.includes('--help-cf')) { console.log(wb.help('worksheet.conditionalFormattings',{include:'index,examples,notes',maxChars:7000}).ndjson); process.exit(0); }
console.log((await wb.inspect({kind:'table',range:'2026年09!A5:K10',include:'values,formulas',maxChars:2000})).ndjson);
const before=await wb.render({sheetName:s.name,range:'A5:K15',scale:1.5});
await fs.writeFile(new URL('before.png',import.meta.url),new Uint8Array(await before.arrayBuffer()));
if(process.argv.includes('--inspect')) process.exit(0);
s.getRange('A38:K38').copyFrom(s.getRange('A37:K37'),'all');
s.getRange('C38:K38').clear({applyTo:'contents'});
s.getRange('A8:K38').conditionalFormats.deleteAll();
s.getRange('A8:K38').format.fill='#FFFFFF';
s.getRange('F8:F38').format.fill='#F4B183';
for(let r=8;r<=38;r++){
 s.getRange(`A${r}`).formulas=[[`=IF(ROW()-7<=DAY(DATE($A$5,$B$5+1,0)),DATE($A$5,$B$5,ROW()-7),"")`]];
 s.getRange(`B${r}`).formulas=[[`=IF(A${r}="","",TEXT(A${r},"ddd"))`]];
}
s.getRange('A8:A38').setNumberFormat('yyyy"年"m"月"d"日"');
s.getRange('A8:K38').conditionalFormats.addCustom('AND(ISNUMBER($A8),WEEKDAY($A8,2)>5)',{fill:'#BFBFBF',font:{color:'#000000'}});
s.getRange('A8:K38').conditionalFormats.addCustom('$A8=""',{fill:'#FFFFFF',font:{color:'#FFFFFF'}});
wb.recalculate();
console.log((await wb.inspect({kind:'table',range:'2026年09!A8:B38',include:'values,formulas',tableMaxRows:31,maxChars:4000})).ndjson);
const after=await wb.render({sheetName:s.name,range:'A5:K16',scale:1.5});
await fs.writeFile(new URL('after.png',import.meta.url),new Uint8Array(await after.arrayBuffer()));
await (await SpreadsheetFile.exportXlsx(wb)).save(fileURLToPath(new URL('TTSKVN_AutoMonth.xlsx',import.meta.url)));
console.log('EXPORTED');
