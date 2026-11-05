import sys, random, datetime
from common import *
from seed_setup import SCHOOLS, CLASSES, sections, periods

TODAY = (datetime.datetime.utcnow() + datetime.timedelta(hours=5, minutes=30)).date()
D = lambda d: d.isoformat()

def students(key):
    cfg = SCHOOLS[key]; rnd = random.Random(cfg['seed'] + 1000)
    stf = f'state_{key}.json'; st = json.load(open(stf)); sid = st['schoolId']
    s = Sess(sid, st['adminPassword'])
    def save(): json.dump(st, open(stf, 'w'), indent=1)
    st.setdefault('students', []); done = {(x['class'], x['section'], x['ord']) for x in st['students']}
    gm = iter(range(9200000000 + cfg['seed']*10000, 9200000000 + cfg['seed']*10000 + 9000))
    used_names = {x['name'] for x in st['students']}
    cats = [3, 3, 3, 3, 4, 4, 5]  # General / OBC / SC weights
    sib_pairs = {('Class 4', 'A'): ('Class 1', 'A'), ('Class 7', 'A'): ('Class 3', 'A'), ('Class 9', 'A'): ('Class 6', 'A')}  # second-enrolled sibling -> first
    sib_of = {}  # younger key -> elder student record (filled when the elder exists)

    def new_person(cls_i):
        while True:
            female = rnd.random() < 0.5
            fn = rnd.choice(cfg['first_f'] if female else cfg['first_m']); ln = rnd.choice(cfg['last'])
            if f'{fn} {ln}' not in used_names: used_names.add(f'{fn} {ln}'); break
        age = 3 + cls_i
        dob = datetime.date(2026 - age, rnd.randint(1, 12), rnd.randint(1, 28))
        if dob >= datetime.date(2026 - age, 7, 1) and age <= 3: dob = datetime.date(2022, 3, rnd.randint(1, 28))  # Nursery: keep ≥3 yrs old
        return dict(fn=fn, ln=ln, female=female, dob=dob)

    def guardian_for(p):
        rel = 'FATHER' if rnd.random() < 0.7 else 'MOTHER'
        gf = rnd.choice(cfg['first_m'] if rel == 'FATHER' else cfg['first_f'])
        return dict(guardianName=f'{gf} {p["ln"]}', guardianRelationship=rel, guardianMobileNumber=str(next(gm)))

    def person_body(p, g):
        return dict(firstName=p['fn'], lastName=p['ln'], dateOfBirth=D(p['dob']) + 'T00:00:00', genderId=2 if p['female'] else 1,
                    bloodGroupId=rnd.choice([1, 3, 5, 7, 7, 3]), studentCategoryId=rnd.choice(cats), **g)

    def fee_items(): return [{'feeTypeId': st['feeTypes']['TRANSPORT'], 'isApplicable': True}] if rnd.random() < 0.25 else None

    SECS = sections(); roll = {}
    # admission-pipeline students: one per these sections, full application -> review -> approve -> confirm
    PIPELINE = {('Nursery', 'A'), ('LKG', 'A'), ('Class 1', 'A'), ('Class 6', 'A'), ('Class 9', 'A')}
    for (c, sn) in SECS:
        ci = CLASSES.index(c); n_target = 10 if sn == 'B' else 12
        secid = st['sections'][f'{c}|{sn}']
        for k in range(n_target):
            if (c, sn, k) in done: continue
            roll[(c, sn)] = k + 1
            p = new_person(ci); g = guardian_for(p); sibling = False
            if (c, sn) in sib_pairs and k == n_target - 1:   # younger sibling: reuse the elder's guardian + surname
                elder = [x for x in st['students'] if (x['class'], x['section']) == sib_pairs[(c, sn)] and x['ord'] == 11]
                if elder:
                    e = elder[0]; p['ln'] = e['name'].split()[-1]; used_names.discard(f'{p["fn"]} {p["ln"]}')
                    g = dict(guardianName=e['guardian'], guardianRelationship=e['guardianRel'], guardianMobileNumber=e['guardianMobile']); sibling = True
            body = person_body(p, g)
            via = 'existing'
            if (c, sn) in PIPELINE and k == 0:
                via = 'admission'
                a = s('POST', f'/Admission/{sid}/submit', dict(academicYearId=st['ay'], appliedClassId=st['classes'][c], admissionTypeId=st['admtype']['New Admission'],
                    applicantFirstName=p['fn'], applicantLastName=p['ln'], dateOfBirth=body['dateOfBirth'], genderId=body['genderId'], bloodGroupId=body['bloodGroupId'], studentCategoryId=body['studentCategoryId'],
                    addressLine1=f'{rnd.randint(1, 99)} Model Colony', pincode='248001' if key == 'LAS' else '247667', previousSchool=None if ci < 4 else 'Little Flowers Public School', **g))[1]['data']
                aid = a['admissionId']
                s('POST', f'/Admission/{sid}/{aid}/start-review', {'notes': 'Documents verified at the front desk.'})
                s('POST', f'/Admission/{sid}/{aid}/approve', {'remarks': 'Approved by the Principal.'})
                r = s('POST', f'/Admission/{sid}/{aid}/confirm', dict(classSectionId=secid, rollNumber=str(k + 1), feeItems=fee_items()))[1]['data']
                stid = r['studentId']; fee = r.get('feeStructureName'); warn = r.get('warnings')
            else:
                r = s('POST', f'/StudentOnboarding/{sid}/existing-student', dict(admissionDate=f'2026-04-{rnd.randint(1, 15):02d}T00:00:00', academicYearId=st['ay'], classSectionId=secid,
                    rollNumber=str(k + 1), feeItems=fee_items(), **body))[1]['data']
                stid = r['studentId']; fee = r.get('feeStructureName'); warn = r.get('warnings')
            rec = dict(studentId=stid, name=f'{p["fn"]} {p["ln"]}', **{'class': c}, section=sn, ord=k, via=via, fee=fee, warnings=warn,
                       guardian=g['guardianName'], guardianRel=g['guardianRelationship'], guardianMobile=g['guardianMobileNumber'], sibling=sibling, login=None)
            if sibling: s('POST', f'/StudentFee/{stid}/concessions', {'concessionId': st['concessions']['SIBLING'], 'remarks': 'Sibling studying in the same school'})
            st['students'].append(rec); done.add((c, sn, k))
            if len(st['students']) % 20 == 0: save()
    save(); print('students', len(st['students']))

    # extra applications that stay in the pipeline (dashboards / admission list)
    if not st.get('pendingApps'):
        st['pendingApps'] = []
        for (c, how) in [('Class 3', 'submit'), ('Class 7', 'submit'), ('Class 2', 'review'), ('Class 4', 'reject')]:
            p = new_person(CLASSES.index(c)); g = guardian_for(p)
            a = s('POST', f'/Admission/{sid}/submit', dict(academicYearId=st['ay'], appliedClassId=st['classes'][c], admissionTypeId=st['admtype']['New Admission'], applicantFirstName=p['fn'], applicantLastName=p['ln'],
                dateOfBirth=D(p['dob']) + 'T00:00:00', genderId=2 if p['female'] else 1, **g))[1]['data']
            if how in ('review', 'reject'): s('POST', f'/Admission/{sid}/{a["admissionId"]}/start-review', {'notes': 'Under review'})
            if how == 'reject': s('POST', f'/Admission/{sid}/{a["admissionId"]}/reject', {'remarks': 'Seats full for this class.'})
            st['pendingApps'].append({'class': c, 'state': how, 'applicationNumber': a['applicationNumber']})
        save()

    # concessions beyond sibling: staff ward + merit on one student each
    if not st.get('extraConcessions'):
        pick = [x for x in st['students'] if x['class'] == 'Class 2'][3]; s('POST', f'/StudentFee/{pick["studentId"]}/concessions', {'concessionId': st['concessions']['STAFFWARD'], 'remarks': 'Ward of a staff member'})
        pick2 = [x for x in st['students'] if x['class'] == 'Class 8'][4]; s('POST', f'/StudentFee/{pick2["studentId"]}/concessions', {'concessionId': st['concessions']['MERIT'], 'remarks': 'Topper of previous session'})
        st['extraConcessions'] = [pick['studentId'], pick2['studentId']]; save()

    # student logins: 3 per class (2 from A + 1 from B for the two-section classes), prefer the admission-pipeline student
    for c in CLASSES:
        secs = [sn for (cc, sn) in SECS if cc == c]
        chosen = [x for x in st['students'] if x['class'] == c and x['section'] == 'A'][:3 if len(secs) == 1 else 2]
        if len(secs) == 2: chosen += [x for x in st['students'] if x['class'] == c and x['section'] == 'B'][:1]
        for x in chosen:
            if x['login']: continue
            r = s('POST', f'/Student/{sid}/{x["studentId"]}/create-login')[1]['data']
            x['login'] = {'loginId': r['orgUserId'], 'password': r['temporaryPassword']}
    save(); print('student logins', sum(1 for x in st['students'] if x['login']))

if __name__ == '__main__': students(sys.argv[1])
