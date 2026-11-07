"""
Report card demo data for ONE of the two demo schools (Little Angels School or Green Hills School).

  python seed-report-card-demo.py LAS --i-have-a-backup [--classes "Class 3,Class 5,Class 8"]

What it does, for each class named, through the normal API (so every rule and audit row of the real screens applies):
  1. creates a Term-wise scheme from the preset (two terms, PT 1 + PT 2 + Half Yearly / Annual, out of 100) with the TERM SPREAD design, attendance entered by hand;
  2. enters invented marks for EVERY student of every section in every exam (a few absences, one struggling student per section), submits and accepts each grid;
  3. fills the co-scholastic and discipline grades, the class teacher's remark, conduct and attendance days of every student for both terms, and the promotion decisions;
  4. LOCKS both terms for each section.
It does NOT publish anything: publishing is the School Admin's decision, made on the Results and Publish screen.

Safety:
  * It refuses to run for any school other than Little Angels School and Green Hills School (checked against the school record, not only the argument).
  * It refuses to run without --i-have-a-backup: take a database backup first (pg_dump -Fc).
  * It is re-runnable: a class whose scheme already exists is skipped, never doubled.
  * All data is invented. Names come from the students already in the demo schools; nothing real is used.
  * SMSS_API_BASE and SMSS_DB (see common.py) point it at a scratch copy instead of the real dev API and database; SMSS_ADMIN_PASSWORD overrides the password in the state file.
"""
import sys, json, random, argparse, datetime
from common import *
from seed_setup import SCHOOLS

ALLOWED_SCHOOLS = ('Little Angels School', 'Green Hills School')
REMARKS_GOOD = ['Excellent work this term. Keep it up.', 'A bright and attentive student who participates well in class.', 'Consistent effort and good results.', 'Very good progress; reading habit is commendable.']
REMARKS_OK = ['Good effort. Regular practice will bring further improvement.', 'Satisfactory progress; should revise written work more often.', 'Participates well; needs to improve handwriting and speed.', 'Doing well. Can do better with more focus in class.']
REMARKS_WEAK = ['Needs regular practice and more attention in class.', 'Must work harder and complete homework on time.', 'Needs extra support in the main subjects; parents are requested to meet the class teacher.']
CONDUCT = ['Very good', 'Good', 'Good', 'Satisfactory']


def die(msg):
    print('REFUSED:', msg); sys.exit(2)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('school', choices=sorted(SCHOOLS.keys()), help='LAS (Little Angels School) or GHS (Green Hills School)')
    ap.add_argument('--classes', default='Class 3,Class 5,Class 8', help='comma-separated class names, default "Class 3,Class 5,Class 8"')
    ap.add_argument('--template', default='term-spread', help='report card design for the new schemes (default term-spread)')
    ap.add_argument('--i-have-a-backup', action='store_true', help='required: confirms a database backup was taken before this run')
    args = ap.parse_args()
    if not args.i_have_a_backup: die('take a database backup first (pg_dump -Fc), then run again with --i-have-a-backup.')
    key = args.school; cfg = SCHOOLS[key]
    if cfg['name'] not in ALLOWED_SCHOOLS: die(f'{cfg["name"]} is not one of the two demo schools this script may touch.')
    stf = f'state_{key}.json'; st = json.load(open(stf))
    school_id = st['schoolId']
    recorded = sql(f"select school_name from tb_schools where school_id='{school_id}'").strip()
    if recorded not in ALLOWED_SCHOOLS or recorded != cfg['name']:
        die(f'school {school_id} is recorded as "{recorded}", not "{cfg["name"]}". This script only runs for {" and ".join(ALLOWED_SCHOOLS)}.')

    s = Sess(school_id, os.environ.get('SMSS_ADMIN_PASSWORD') or st['adminPassword'])
    ay = st['ay']; class_ids = st['classes']; sections = st['sections']
    rnd = random.Random(cfg['seed'] + 3000)
    wanted = [c.strip() for c in args.classes.split(',') if c.strip()]
    for c in wanted:
        if c not in class_ids: die(f'"{c}" is not a class of {cfg["name"]}.')
    order = list(class_ids.keys())
    report = {'school': cfg['name'], 'schoolId': school_id, 'classes': {}}
    print(f'Seeding report card demo data for {cfg["name"]} ({school_id}): {", ".join(wanted)}')
    s('POST', '/ReportActivityArea/starter', must=False)                      # the starter co-scholastic / discipline areas (a second call is harmless)

    existing = {x['schemeName'] for x in s('GET', '/ReportScheme')[1]['data']}
    for cname in wanted:
        sname = f'Demo Term-wise {cname}'
        if sname in existing: print(f'  {cname}: scheme "{sname}" already exists, skipped'); continue
        c, j = s('POST', '/ReportScheme/from-preset', {'presetCode': 'term-wise-100', 'academicYearId': ay, 'schemeName': sname, 'classIds': [class_ids[cname]]})
        sch = j['data']
        s('PUT', f'/ReportScheme/{sch["schemeId"]}', {'name': sname, 'academicYearId': ay, 'evaluationMode': sch['evaluationMode'], 'gradeScaleId': sch['gradeScaleId'], 'yearCombine': sch['yearCombine'],
                                                       'subjectPassPercent': sch['subjectPassPercent'], 'overallPassPercent': sch['overallPassPercent'], 'showRank': sch['showRank'], 'rankScope': sch['rankScope'],
                                                       'attendanceSource': 'MANUAL', 'percentageRounding': sch['percentageRounding'], 'templateKey': args.template})
        sch = s('GET', f'/ReportScheme/{sch["schemeId"]}')[1]['data']
        terms = sch['terms']; cells = [x for x in sch['cells'] if x['classId'] == class_ids[cname]]
        next_class = order[order.index(cname) + 1] if order.index(cname) + 1 < len(order) else 'Class 11'
        info = {'schemeId': sch['schemeId'], 'sections': {}}
        print(f'  {cname}: scheme "{sname}" created (id {sch["schemeId"]}), {len(cells)} exam subjects, {len(terms)} terms')
        for sec_key, sec_id in sections.items():
            if not sec_key.startswith(cname + '|'): continue
            first = s('GET', f'/ReportMarks/grid?examSubjectId={cells[0]["examSubjectId"]}&sectionId={sec_id}')[1]['data']
            students = [x['studentId'] for x in first['students']]
            if not students: continue
            # one ability per student (0..1), with one struggling student per section
            ability = {sid: min(0.97, max(0.35, rnd.gauss(0.72, 0.13))) for sid in students}
            weak = students[rnd.randrange(len(students))]; ability[weak] = 0.28
            n_marks = 0
            for cell in cells:
                if cell['entryMode'] != 'MARKS': continue
                mx = float(cell['effectiveMax'])
                g = s('GET', f'/ReportMarks/grid?examSubjectId={cell["examSubjectId"]}&sectionId={sec_id}')[1]['data']
                entries = []
                for sid in students:
                    if rnd.random() < 0.012 and sid != weak:
                        entries.append({'studentId': sid, 'state': 'ABSENT'}); continue
                    m = mx * min(1.0, max(0.05, ability[sid] + rnd.gauss(0, 0.07)))
                    m = round(m) if mx <= 10 else round(m * 2) / 2
                    entries.append({'studentId': sid, 'state': 'ENTERED', 'marks': float(min(m, mx))}); n_marks += 1
                g = s('PUT', '/ReportMarks/grid', {'examSubjectId': cell['examSubjectId'], 'sectionId': sec_id, 'version': g['version'], 'entries': entries})[1]['data']
                g = s('POST', '/ReportMarks/submit', {'examSubjectId': cell['examSubjectId'], 'sectionId': sec_id, 'version': g['version']})[1]['data']
                s('POST', '/ReportMarks/accept', {'examSubjectId': cell['examSubjectId'], 'sectionId': sec_id, 'version': g['version']})
            # the class teacher's inputs, per term
            for t in terms:
                ag = s('GET', f'/ReportClass/activity-grades?termId={t["termId"]}&sectionId={sec_id}')[1]['data']
                grades = []
                for sid in students:
                    for area in ag['areas']:
                        codes = [o['code'] for o in area['options']]
                        pick = min(len(codes) - 1, max(0, int(round((1 - ability[sid]) * (len(codes) - 1) + rnd.gauss(0, 0.5)))))
                        grades.append({'studentId': sid, 'areaId': area['areaId'], 'gradeCode': codes[pick]})
                if grades: s('PUT', '/ReportClass/activity-grades', {'termId': t['termId'], 'sectionId': sec_id, 'grades': grades})
                working = 88 if t['sequence'] == 1 else 94
                entries = []
                for sid in students:
                    a = ability[sid]
                    pool = REMARKS_GOOD if a >= 0.8 else REMARKS_OK if a >= 0.5 else REMARKS_WEAK
                    entries.append({'studentId': sid, 'remark': rnd.choice(pool), 'conduct': rnd.choice(CONDUCT) if a >= 0.5 else 'Satisfactory',
                                    'manualWorkingDays': working, 'manualPresentDays': min(working, int(working * min(0.99, max(0.6, rnd.gauss(0.91, 0.06)))))})
                s('PUT', '/ReportClass/term-students', {'termId': t['termId'], 'sectionId': sec_id, 'students': entries})
            decisions = [{'studentId': sid, 'promotionDecision': 'NOT_PROMOTED' if sid == weak else 'PROMOTED', 'promotedTo': None if sid == weak else next_class,
                          'newSessionDate': '2027-04-01'} for sid in students]
            s('PUT', '/ReportClass/year-students', {'schemeId': sch['schemeId'], 'sectionId': sec_id, 'students': decisions})
            locked = []
            for t in terms:
                c2, j2 = s('POST', '/ReportClass/lock', {'termId': t['termId'], 'sectionId': sec_id, 'confirmExcluded': False}, ok=(200, 409), must=True)
                locked.append(bool(j2.get('data') and j2['data'].get('locked')))
            info['sections'][sec_key] = {'sectionId': sec_id, 'students': len(students), 'weakStudent': weak, 'marksEntered': n_marks, 'termsLocked': locked}
            print(f'    {sec_key}: {len(students)} students, {n_marks} marks entered, terms locked {locked}')
        report['classes'][cname] = info
    json.dump(report, open(f'state_{key}_reportcards.json', 'w'), indent=1)
    print('Done. Nothing was published. Open Examinations > Results and Publish to review and publish.')


if __name__ == '__main__':
    main()
