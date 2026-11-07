"""
GRADES-mode report card demo data for ONE of the two demo schools (Little Angels School or Green Hills School).

  python seed-report-card-grades-demo.py LAS --i-have-a-backup

What it does, through the normal API (so every rule and audit row of the real screens applies):
  1. Subjects and class-subject mappings are created ONLY IF MISSING, and the script prints exactly what it created:
       UKG       English, Hindi, Mathematics, EVS
       Nursery, LKG   Writing, Reading, Speaking, Language, Numbering, Art & Craft (already there), Playing and Sharing
  2. UKG: a pre-primary SKILL-GRID scheme (template skill-grid). Subjects English, Hindi, Mathematics, EVS, each with the skills Reading, Writing, Oral, Cycle-wise grade,
     Internal assessment; exams PT 1, PT 2, Half Yearly (Term I) and PT 3, PT 4, Final (Term II); the school's A-B-C rating scale (A Outstanding, B Very Good, C Fair);
     activity areas Discipline and Cleanliness; term remarks, rank text written by hand, promotion and "new session begins", attendance entered by hand.
  3. Nursery and LKG: a DESCRIPTIVE scheme (template playful-report). The learning areas are the subjects; one assessment per term; subject remarks and "things to improve".
  4. Class 6 (classic-card), Class 7 (minimal-bw), Class 9 and Class 10 (board-style): MARKS schemes with marks, remarks and conduct, by running seed-report-card-demo.py with --template.
     Together with the existing Class 3, 5 and 8 schemes (term-spread) every design has a scheme with data in each demo school.
  5. Grades are entered for every student, every grid is submitted and accepted, and the terms are LOCKED. NOTHING IS PUBLISHED: publishing is the School Admin's decision.

Safety (same as the other demo seeds):
  * It refuses to run for any school other than Little Angels School and Green Hills School (checked against the school record, not only the argument).
  * It refuses to run without --i-have-a-backup: take a database backup first (pg_dump -Fc).
  * It is re-runnable: a scheme that already exists is skipped, never doubled.
  * All data is invented. Names come from the students already in the demo schools.
  * SMSS_API_BASE and SMSS_DB (see common.py) point it at a scratch copy; SMSS_ADMIN_PASSWORD overrides the password in the state file.
"""
import sys, json, random, argparse, subprocess, os
from common import *
from seed_setup import SCHOOLS

ALLOWED_SCHOOLS = ('Little Angels School', 'Green Hills School')
UKG_SUBJECTS = ['English', 'Hindi', 'Mathematics', 'EVS']
UKG_SKILLS = ['Reading', 'Writing', 'Oral', 'Cycle-wise grade', 'Internal assessment']
AREAS = ['Writing', 'Reading', 'Speaking', 'Language', 'Numbering', 'Art & Craft', 'Playing and Sharing']
SUBJECT_CODES = {'Writing': 'WRT', 'Reading': 'RDG', 'Speaking': 'SPK', 'Language': 'LNG', 'Numbering': 'NUM', 'Playing and Sharing': 'PLS'}
REMARK_GOOD = ['Wonderful progress this term. Loves to take part.', 'A happy, confident learner who helps friends.', 'Very good work; keep reading at home.']
REMARK_OK = ['Good effort. A little more practice will help.', 'Is settling in well and enjoys group activities.', 'Participates nicely; needs more practice with writing.']
REMARK_WEAK = ['Needs regular practice and gentle encouragement.', 'Needs extra attention with letters and numbers; please practise at home.']
IMPROVE_BY_AREA = {'Writing': ['Hold the pencil correctly', 'Practise joining letters', 'Write on the line'], 'Reading': ['Read aloud every day', 'Recognise more sight words'],
                   'Speaking': ['Speak in full sentences', 'Speak louder and clearly'], 'Language': ['Learn the new rhymes', 'Use new words'],
                   'Numbering': ['Count objects up to 50', 'Practise writing numbers 1 to 20'], 'Art & Craft': ['Colour inside the lines', 'Use scissors carefully'],
                   'Playing and Sharing': ['Wait for a turn', 'Share toys with friends']}
CONDUCT = ['Very good', 'Good', 'Good', 'Satisfactory']
RANK_TEXT = ['1st', '2nd', '3rd', '4th', '5th', '6th', '7th', '8th', '9th', '10th', '11th', '12th']
NEXT = {'Nursery': 'LKG', 'LKG': 'UKG', 'UKG': 'Class 1'}


def die(msg):
    print('REFUSED:', msg); sys.exit(2)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('school', choices=sorted(SCHOOLS.keys()), help='LAS (Little Angels School) or GHS (Green Hills School)')
    ap.add_argument('--i-have-a-backup', action='store_true', help='required: confirms a database backup was taken before this run')
    ap.add_argument('--skip-marks-schemes', action='store_true', help='only the GRADES schemes (skip Class 6, 7, 9 and 10)')
    args = ap.parse_args()
    if not args.i_have_a_backup: die('take a database backup first (pg_dump -Fc), then run again with --i-have-a-backup.')
    key = args.school; cfg = SCHOOLS[key]
    if cfg['name'] not in ALLOWED_SCHOOLS: die(f'{cfg["name"]} is not one of the two demo schools this script may touch.')
    st = json.load(open(f'state_{key}.json')); school_id = st['schoolId']
    recorded = sql(f"select school_name from tb_schools where school_id='{school_id}'").strip()
    if recorded not in ALLOWED_SCHOOLS or recorded != cfg['name']:
        die(f'school {school_id} is recorded as "{recorded}", not "{cfg["name"]}". This script only runs for {" and ".join(ALLOWED_SCHOOLS)}.')

    s = Sess(school_id, os.environ.get('SMSS_ADMIN_PASSWORD') or st['adminPassword'])
    ay = st['ay']; class_ids = st['classes']; sections = st['sections']
    rnd = random.Random(cfg['seed'] + 5000)
    created = {'subjects': [], 'mappings': [], 'schemes': []}
    print(f'Seeding GRADES report card demo data for {cfg["name"]} ({school_id})')
    s('POST', '/ReportActivityArea/starter', must=False)

    # ---- 1. subjects and mappings, only if missing
    subj = {x['subjectName']: x['subjectId'] for x in s('GET', '/Subjects')[1]['data']}
    for name in UKG_SUBJECTS + AREAS:
        if name in subj: continue
        subj[name] = s('POST', '/Subjects', {'subjectName': name, 'subjectCode': SUBJECT_CODES.get(name, name[:3].upper()), 'description': f'{name} (pre-primary)'})[1]['data']['subjectId']
        created['subjects'].append(name)
    mapped = {(class_ids[c], m['subjectId']) for c in ('UKG', 'Nursery', 'LKG') for m in s('GET', f'/ClassSubjects?classId={class_ids[c]}')[1]['data']}
    for cname, names in (('UKG', UKG_SUBJECTS), ('Nursery', AREAS), ('LKG', AREAS)):
        for name in names:
            if (class_ids[cname], subj[name]) in mapped: continue
            s('POST', '/ClassSubjects', {'classId': class_ids[cname], 'subjectId': subj[name]})
            created['mappings'].append(f'{cname}: {name}')
    print('  subjects created:', created['subjects'] or 'none (all existed)')
    print('  class-subject mappings created:', created['mappings'] or 'none (all existed)')

    existing = {x['schemeName']: x for x in s('GET', '/ReportScheme')[1]['data']}

    def make_scheme(name, class_names, template, skills_by_subject, subject_names, one_exam_per_term):
        """The pre-primary preset (GRADES, two terms, the A-B-C scale), trimmed to the wanted subjects, skills and exams."""
        c, j = s('POST', '/ReportScheme/from-preset', {'presetCode': 'pre-primary-skill-grid', 'academicYearId': ay, 'schemeName': name, 'classIds': [class_ids[c] for c in class_names], 'templateKey': template})
        sch = j['data']; sid = sch['schemeId']
        s('PUT', f'/ReportScheme/{sid}', {'name': name, 'academicYearId': ay, 'evaluationMode': 'GRADES', 'gradeScaleId': sch['gradeScaleId'], 'yearCombine': sch['yearCombine'], 'subjectPassPercent': sch['subjectPassPercent'],
                                           'overallPassPercent': sch['overallPassPercent'], 'showRank': False, 'rankScope': sch['rankScope'], 'attendanceSource': 'MANUAL', 'percentageRounding': sch['percentageRounding'], 'templateKey': template})
        sch = s('GET', f'/ReportScheme/{sid}')[1]['data']
        if one_exam_per_term:
            s('PUT', f'/ReportScheme/{sid}/terms', {'terms': [{'termId': t['termId'], 'name': t['name'], 'startDate': t['startDate'], 'endDate': t['endDate'], 'yearWeight': t.get('yearWeight'),
                                                                'exams': [{'examId': t['exams'][-1]['examId'], 'name': 'Term Assessment', 'defaultMaxMarks': 10}]} for t in sch['terms']]})
            sch = s('GET', f'/ReportScheme/{sid}')[1]['data']
        keep = [x for x in sch['subjects'] if x['subjectName'] in subject_names]
        s('PUT', f'/ReportScheme/{sid}/subjects', {'subjects': [{'schemeSubjectId': x['schemeSubjectId'], 'classId': x['classId'], 'subjectId': x['subjectId'], 'displayOrder': subject_names.index(x['subjectName']) + 1,
                                                                'includeInTotal': True} for x in keep]})
        sch = s('GET', f'/ReportScheme/{sid}')[1]['data']
        s('PUT', f'/ReportScheme/{sid}/matrix', {'cells': [{'examId': e['examId'], 'schemeSubjectId': x['schemeSubjectId'], 'entryMode': 'GRADE'} for t in sch['terms'] for e in t['exams'] for x in sch['subjects']]})
        s('PUT', f'/ReportScheme/{sid}/skills', {'subjects': [{'schemeSubjectId': x['schemeSubjectId'], 'skills': skills_by_subject} for x in sch['subjects']]})
        return s('GET', f'/ReportScheme/{sid}')[1]['data']

    def enter_and_lock(sch, class_names, descriptive):
        sid = sch['schemeId']; terms = sch['terms']
        scale = s('GET', f'/GradeScale/{sch["gradeScaleId"]}')[1]['data']
        codes = [b['gradeCode'] for b in sorted(scale['bands'], key=lambda b: b['sequence'])]     # best first? (sorted by sequence: lowest min first in the preset)
        best_first = [c for c in ['A', 'B', 'C'] if c in codes] or codes
        info = {'schemeId': sid, 'sections': {}}
        for cname in class_names:
            cells = [x for x in sch['cells'] if x['classId'] == class_ids[cname]]
            for sec_key, sec_id in sections.items():
                if not sec_key.startswith(cname + '|'): continue
                first = s('GET', f'/ReportMarks/grid?examSubjectId={cells[0]["examSubjectId"]}&sectionId={sec_id}')[1]['data']
                students = [x['studentId'] for x in first['students']]
                if not students: continue
                ability = {x: min(0.97, max(0.3, rnd.gauss(0.72, 0.14))) for x in students}
                weak = students[rnd.randrange(len(students))]; ability[weak] = 0.3
                pick = lambda a: best_first[min(len(best_first) - 1, max(0, int(round((1 - a) * (len(best_first) - 1) + rnd.gauss(0, 0.45)))))]
                n = 0
                for cell in cells:
                    g = s('GET', f'/ReportMarks/grid?examSubjectId={cell["examSubjectId"]}&sectionId={sec_id}')[1]['data']
                    entries = []
                    for sid_ in students:
                        if g['skills']:
                            for k in g['skills']:
                                entries.append({'studentId': sid_, 'skillId': k['skillId'], 'state': 'ENTERED', 'gradeCode': pick(ability[sid_])}); n += 1
                        else:
                            entries.append({'studentId': sid_, 'state': 'ENTERED', 'gradeCode': pick(ability[sid_])}); n += 1
                    body = {'examSubjectId': cell['examSubjectId'], 'sectionId': sec_id, 'version': g['version'], 'entries': entries}
                    # the per-subject remark (and, for the descriptive scheme, "things to improve"): written once per subject and term, with the last exam of the term
                    term = next(t for t in terms if any(e['examId'] == cell['examId'] for e in t['exams']))
                    if cell['examId'] == term['exams'][-1]['examId']:
                        sname = next(x['subjectName'] for x in sch['subjects'] if x['schemeSubjectId'] == cell['schemeSubjectId'])
                        body['remarks'] = []
                        for sid_ in students:
                            a = ability[sid_]
                            r = {'studentId': sid_, 'remark': rnd.choice(REMARK_GOOD if a >= 0.8 else REMARK_OK if a >= 0.55 else REMARK_WEAK)}
                            if descriptive: r['improvement'] = rnd.choice(IMPROVE_BY_AREA.get(sname, ['Practise a little every day'])) if a < 0.85 else ''
                            body['remarks'].append(r)
                    g = s('PUT', '/ReportMarks/grid', body)[1]['data']
                    g = s('POST', '/ReportMarks/submit', {'examSubjectId': cell['examSubjectId'], 'sectionId': sec_id, 'version': g['version']})[1]['data']
                    s('POST', '/ReportMarks/accept', {'examSubjectId': cell['examSubjectId'], 'sectionId': sec_id, 'version': g['version']})
                ranked = sorted(students, key=lambda x: -ability[x])
                for t in terms:
                    ag = s('GET', f'/ReportClass/activity-grades?termId={t["termId"]}&sectionId={sec_id}')[1]['data']
                    grades = []
                    for sid_ in students:
                        for area in ag['areas']:
                            oc = [o['code'] for o in area['options']]
                            grades.append({'studentId': sid_, 'areaId': area['areaId'], 'gradeCode': oc[min(len(oc) - 1, max(0, int(round((1 - ability[sid_]) * (len(oc) - 1) + rnd.gauss(0, 0.4)))))]})
                    if grades: s('PUT', '/ReportClass/activity-grades', {'termId': t['termId'], 'sectionId': sec_id, 'grades': grades})
                    working = 88 if t['sequence'] == 1 else 94
                    ents = []
                    for sid_ in students:
                        a = ability[sid_]
                        ents.append({'studentId': sid_, 'remark': rnd.choice(REMARK_GOOD if a >= 0.8 else REMARK_OK if a >= 0.55 else REMARK_WEAK), 'conduct': rnd.choice(CONDUCT) if a >= 0.5 else 'Satisfactory',
                                     'rankText': RANK_TEXT[min(ranked.index(sid_), len(RANK_TEXT) - 1)], 'manualWorkingDays': working,
                                     'manualPresentDays': min(working, int(working * min(0.99, max(0.6, rnd.gauss(0.92, 0.05)))))})
                    s('PUT', '/ReportClass/term-students', {'termId': t['termId'], 'sectionId': sec_id, 'students': ents})
                nxt = NEXT.get(cname, 'Class 1')
                s('PUT', '/ReportClass/year-students', {'schemeId': sid, 'sectionId': sec_id, 'students': [
                    {'studentId': x, 'promotionDecision': 'PROMOTED', 'promotedTo': nxt, 'newSessionDate': '2027-04-01'} for x in students]})
                locked = []
                for t in terms:
                    c2, j2 = s('POST', '/ReportClass/lock', {'termId': t['termId'], 'sectionId': sec_id, 'confirmExcluded': False}, ok=(200, 409), must=True)
                    locked.append(bool(j2.get('data') and j2['data'].get('locked')))
                info['sections'][sec_key] = {'sectionId': sec_id, 'students': len(students), 'gradesEntered': n, 'termsLocked': locked}
                print(f'    {sec_key}: {len(students)} students, {n} grades entered, terms locked {locked}')
        return info

    report = {'school': cfg['name'], 'schoolId': school_id, 'created': created, 'schemes': {}}
    for name, class_names, template, skills, names, one_exam, descriptive in (
            ('Demo Skill Grid UKG', ['UKG'], 'skill-grid', UKG_SKILLS, UKG_SUBJECTS, False, False),
            ('Demo Descriptive Nursery and LKG', ['Nursery', 'LKG'], 'playful-report', [], AREAS, True, True)):
        if name in existing: print(f'  scheme "{name}" already exists, skipped'); continue
        # a class can be in only one scheme per academic year: a class that already belongs to someone else's scheme is left alone (and said so)
        taken = {c: x['schemeName'] for x in s('GET', '/ReportScheme')[1]['data'] for c in x['classNames']}
        for c in [c for c in class_names if c in taken]: print(f'  NOTE: {c} is already in the scheme "{taken[c]}" for this year; it is left untouched, so "{name}" does not include {c}.')
        class_names = [c for c in class_names if c not in taken]
        if not class_names: continue
        sch = make_scheme(name, class_names, template, skills, names, one_exam)
        created['schemes'].append(name)
        print(f'  scheme "{name}" created (id {sch["schemeId"]}): {len(sch["subjects"])} subject rows, {len(sch["terms"])} terms, {len(sch["cells"])} cells')
        report['schemes'][name] = enter_and_lock(sch, class_names, descriptive)

    if not args.skip_marks_schemes:
        here = os.path.dirname(os.path.abspath(__file__))
        for classes, template in (('Class 6', 'classic-card'), ('Class 7', 'minimal-bw'), ('Class 9,Class 10', 'board-style')):
            r = subprocess.run([sys.executable, os.path.join(here, 'seed-report-card-demo.py'), key, '--i-have-a-backup', '--classes', classes, '--template', template], cwd=os.getcwd(), env=os.environ.copy(), capture_output=True, text=True)
            print(r.stdout.rstrip());
            if r.returncode != 0: print(r.stderr[-800:]); die(f'seed-report-card-demo.py failed for {classes}')
    json.dump(report, open(f'state_{key}_reportcards_grades.json', 'w'), indent=1)
    print('Done. Nothing was published. Created:', json.dumps({k: v for k, v in created.items()}))


if __name__ == '__main__':
    main()
