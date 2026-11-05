import sys, random, datetime
from common import *
from seed_setup import SCHOOLS, CLASSES

TODAY = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(hours=5, minutes=30)).date()
D = lambda d: d.isoformat()

def activity(key):
    cfg = SCHOOLS[key]; rnd = random.Random(cfg['seed'] + 2000)
    stf = f'state_{key}.json'; st = json.load(open(stf)); s = Sess(st['schoolId'], st['adminPassword'])
    name = cfg['name']; act = st.setdefault('activity', {})
    def save(): json.dump(st, open(stf, 'w'), indent=1)
    # notices
    if 'notices' not in act:
        a = s('POST', '/Notice', {'title': f'Welcome to the new session 2026-27 at {name}', 'body': 'Dear parents and students, we welcome you to the academic session 2026-27. School timings are 8:30 AM to 12:50 PM, Monday to Saturday. Please ensure students are in proper uniform.', 'audience': 'ALL'})[1]['data']
        b = s('POST', '/Notice', {'title': 'Staff meeting and syllabus planning', 'body': 'All teaching staff are requested to attend a staff meeting on Saturday after school hours to finalise the half-yearly syllabus and examination schedule.', 'audience': 'STAFF'})[1]['data']
        c = s('POST', '/Notice', {'title': 'Fee payment reminder', 'body': 'Parents are requested to clear pending tuition fee dues before the 10th of the month to avoid late charges.', 'audience': 'STUDENTS_GUARDIANS'})[1]['data']
        act['notices'] = [a['noticeId'], b['noticeId'], c['noticeId']]; save()
    # calendar events
    if 'events' not in act:
        ev = [('Diwali Holiday', '2026-11-08', '2026-11-10', 1, 'School remains closed for Diwali.'),
              ('Parent-Teacher Meeting', '2026-11-21', None, 3, 'PTM for all classes, 9:00 AM to 12:00 PM.'),
              ('Half-Yearly Examinations', '2026-12-07', '2026-12-19', 2, 'Half-yearly examinations for Class 1 to Class 10.'),
              ('Annual Day Celebration', '2027-01-23', None, 5, 'Annual day function in the school auditorium.')]
        act['events'] = [s('POST', '/SchoolEvent', dict(eventTitle=t, startDate=a, endDate=b, eventCategoryId=cat, description=d, isAllDay=True))[1]['data']['eventId'] for t, a, b, cat, d in ev]; save()
    # homework + assessments
    plan = st['teacherOf']; emp = lambda k: st['employees'][k]['employeeId']
    by_sec = {}
    for x in st['students']: by_sec.setdefault((x['class'], x['section']), []).append(x)
    if 'homework' not in act:
        act['homework'] = []
        for (c, subj, title, desc) in [('Class 3', 'MAT', 'Multiplication tables 2 to 12', 'Write tables 2 to 12 in the notebook and practise orally.'),
                                       ('Class 6', 'MAT', 'Fractions worksheet', 'Solve exercise 4.2, questions 1 to 10.'),
                                       ('Class 8', 'ENG', 'Letter writing', 'Write a formal letter to the Principal requesting a sports day.'),
                                       ('Class 10', 'SCI', 'Chemical reactions: numericals', 'Complete the balancing equations worksheet.')]:
            k = plan[f'{c}|A|{subj}']
            r = s('POST', '/Homework', dict(sectionId=st['sections'][f'{c}|A'], subjectId=st['subjects'][subj], employeeId=emp(k), title=title, description=desc, assignedDate=D(TODAY), dueDate=D(TODAY + datetime.timedelta(days=3))))[1]['data']
            act['homework'].append(r['homeworkId'])
        save()
    if 'assessments' not in act:
        act['assessments'] = []
        for (c, subj, title, mx) in [('Class 3', 'MAT', 'Unit Test 1: Mathematics', 20), ('Class 6', 'SCI', 'Unit Test 1: Science', 25), ('Class 8', 'ENG', 'Class Test: Grammar', 20), ('Class 10', 'MAT', 'Unit Test 2: Mathematics', 40)]:
            k = plan[f'{c}|A|{subj}']; roster = by_sec[(c, 'A')]; absent = rnd.choice(roster)['studentId']
            marks = [{'studentId': x['studentId'], 'isAbsent': x['studentId'] == absent, 'obtainedMarks': None if x['studentId'] == absent else round(rnd.uniform(0.45, 0.98) * mx * 2) / 2} for x in roster]
            r = s('POST', '/Assessment', dict(academicYearId=st['ay'], sectionId=st['sections'][f'{c}|A'], subjectId=st['subjects'][subj], assessmentTypeId=2, employeeId=emp(k), title=title,
                testDate=D(TODAY - datetime.timedelta(days=rnd.randint(2, 5))), maxMarks=mx, marks=marks))[1]['data']
            act['assessments'].append(r['assessmentId'])
        save()
    # attendance: today via the normal submit, previous working days via School Admin corrections (the API only accepts "today" on submit)
    if 'attendance' not in act:
        days = []; d = TODAY - datetime.timedelta(days=1)
        while len(days) < 6:
            if d.weekday() != 6: days.append(d)
            d -= datetime.timedelta(days=1)
        secs = [('Nursery', 'A'), ('Class 3', 'A'), ('Class 6', 'A'), ('Class 8', 'A'), ('Class 10', 'A'), ('Class 10', 'B')]
        done = 0
        for (c, sn) in secs:
            roster = by_sec[(c, sn)]; secid = st['sections'][f'{c}|{sn}']
            if TODAY.weekday() != 6:
                s('POST', '/Attendance/submit', dict(sectionId=secid, date=D(TODAY), records=[{'studentId': x['studentId'], 'attendanceStatusId': 2 if rnd.random() < 0.08 else 1} for x in roster]))
            for day in days:
                for x in roster:
                    if x['via'] == 'admission': continue   # admitted today: no enrollment before today to attend
                    sts = 2 if rnd.random() < 0.08 else 1
                    c_, j_ = s('PUT', '/Attendance/correct', dict(studentId=x['studentId'], date=D(day), attendanceStatusId=sts, reason='Demo data: historical attendance entry'), must=False)
                    if c_ == 200: done += 1
                    elif 'already marked' not in (j_.get('message') or ''): raise RuntimeError(j_.get('message'))
        act['attendance'] = {'sections': [f'{c}|{sn}' for c, sn in secs], 'days': [D(x) for x in days], 'correctionRows': done}; save()
    print(key, 'activity', {k: (len(v) if isinstance(v, list) else v) for k, v in act.items()})

if __name__ == '__main__': activity(sys.argv[1])
