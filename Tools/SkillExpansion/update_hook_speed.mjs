import fs from 'node:fs/promises';
import path from 'node:path';
import { FileBlob, SpreadsheetFile } from '@oai/artifact-tool';

const root = path.resolve(import.meta.dirname, '../..');
const filename = path.join(root, 'GameData/GameData_Skill.xlsx');
const report = path.join(root, 'Reports/BruteHook');
await fs.mkdir(report, { recursive: true });
const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(filename));
const snapshot = book => book.worksheets.items.map(s => ({ name: s.name, values: s.getUsedRange().values, formulas: s.getUsedRange().formulas }));
const expected = snapshot(workbook);
const params = workbook.worksheets.getItem('SkillParameter');
const rows = params.getUsedRange().values;
const changes = [['ProjectileSpeed', 12, 18], ['PullSpeed', 8, 12], ['RetrieveSeconds', .6, .4]];
const edits = changes.map(([key, before, after]) => {
    const row = rows.findIndex(r => r[0] === 'STR_Hook' && r[1] === key);
    if (row < 0 || ![before, after].includes(rows[row][2])) throw new Error('Unexpected hook parameter: ' + key);
    return { row, key, before: rows[row][2], after };
});
const first = rows.findIndex(r => r[0] === 'STR_Hook') + 1;
const preview = async suffix => {
    const blob = await workbook.render({ sheetName: 'SkillParameter', range: `A${first}:D${first+7}`, scale: 1, format: 'png' });
    await fs.writeFile(path.join(report, `hook-data-${suffix}.png`), new Uint8Array(await blob.arrayBuffer()));
};
if (process.argv.includes('--preview')) {
    await preview('before');
    console.log(JSON.stringify(edits));
} else {
    for (const edit of edits) {
        params.getRangeByIndexes(edit.row, 2, 1, 1).values = [[edit.after]];
        expected.find(s => s.name === params.name).values[edit.row][2] = edit.after;
    }
    workbook.recalculate();
    await preview('after');
    const errors = await workbook.inspect({ kind: 'match', searchTerm: '#REF!|#DIV/0!|#VALUE!|#NAME\\?|#NUM!|#NULL!', options: { useRegex: true, maxResults: 10 }, maxChars: 1000 });
    const output = await SpreadsheetFile.exportXlsx(workbook);
    await output.save(filename);
    const saved = await SpreadsheetFile.importXlsx(await FileBlob.load(filename));
    if (JSON.stringify(snapshot(saved)) !== JSON.stringify(expected)) throw new Error('Unexpected workbook value/formula changes');
    await fs.writeFile(path.join(report, 'hook-data-validation.json'), JSON.stringify({ edits, preservedOtherValuesAndFormulas: true, errors: errors.ndjson }, null, 2));
    console.log(JSON.stringify(edits));
}
