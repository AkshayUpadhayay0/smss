import sys, random, datetime
from common import *

SCHOOLS = {
 'LAS': dict(name='Little Angels School', pw='Admin@Demo26', seed=11,
   first_m=['Aarav','Vivaan','Aditya','Arjun','Reyansh','Krishna','Ishaan','Rohan','Kabir','Dev','Yash','Nikhil','Harsh','Karan','Manav','Pranav','Sahil','Tanmay','Utkarsh','Vihaan'],
   first_f=['Aanya','Diya','Saanvi','Ananya','Myra','Pari','Kiara','Riya','Ishita','Navya','Tanvi','Mehak','Aditi','Prisha','Sara','Meera','Kavya','Nisha','Shreya','Anika'],
   last=['Rawat','Negi','Bisht','Joshi','Bhatt','Chauhan','Thapa','Panwar','Rana','Semwal','Uniyal','Nautiyal','Gusain','Kandpal','Dobhal'],
   staff=['Sunita Rawat','Meena Negi','Pooja Bisht','Anita Joshi','Ramesh Bhatt','Deepa Chauhan','Suresh Thapa','Neha Panwar','Vikram Rana','Priya Semwal','Manoj Uniyal','Kiran Nautiyal','Seema Gusain','Anil Kandpal','Rekha Dobhal','Sandeep Rawat','Nidhi Negi','Rahul Bisht','Alka Joshi','Gaurav Bhatt','Mamta Chauhan'],
   principal='Kavita Sharma', accountant='Rajesh Bisht', admission='Swati Thapa', librarian='Hemant Panwar', clerk='Dinesh Rana'),
 'GHS': dict(name='Green Hills School', pw='Admin@Demo26', seed=22,
   first_m=['Aryan','Atharv','Dhruv','Ayaan','Laksh','Shaurya','Veer','Rudra','Samar','Neel','Parth','Mohit','Ankit','Sumit','Tarun','Vedant','Kunal','Lakshay','Rishi','Naman'],
   first_f=['Aarohi','Avni','Ira','Jiya','Khushi','Lavanya','Mahi','Naina','Pihu','Rhea','Sanvi','Trisha','Vanya','Zoya','Bhavya','Charvi','Disha','Esha','Gauri','Hiral'],
   last=['Verma','Gupta','Saini','Kumar','Tyagi','Malik','Singh','Sharma','Pundir','Kashyap','Mittal','Goyal','Agarwal','Chaudhary','Tomar'],
   staff=['Anjali Verma','Rakesh Gupta','Sonia Saini','Amit Kumar','Pankaj Tyagi','Ritu Malik','Harpreet Singh','Shalini Sharma','Vinod Pundir','Babita Kashyap','Mukesh Mittal','Preeti Goyal','Sachin Agarwal','Jyoti Chaudhary','Ajay Tomar','Komal Verma','Naveen Gupta','Poonam Saini','Deepak Kumar','Sunil Tyagi','Rachna Malik'],
   principal='Anil Verma', accountant='Sanjay Goyal', admission='Isha Mittal', librarian='Mahesh Pundir', clerk='Lalit Kashyap'),
}
FEM = {'Sunita','Meena','Pooja','Anita','Deepa','Neha','Priya','Kiran','Seema','Rekha','Nidhi','Alka','Mamta','Anjali','Sonia','Ritu','Shalini','Babita','Preeti','Jyoti','Komal','Poonam','Rachna','Kavita','Swati','Isha'}
CLASSES = ['Nursery','LKG','UKG'] + [f'Class {i}' for i in range(1,11)]
CCODE = ['NUR','LKG','UKG'] + [f'C{i}' for i in range(1,11)]
TWO_SECTION = {'Class 5','Class 10'}
SUBJ = {'ENG':'English','HIN':'Hindi','MAT':'Mathematics','SCI':'Science','SST':'Social Studies','EVS':'EVS','CMP':'Computer Science',
 'SAN':'Sanskrit','ART':'Art & Craft','PE':'Physical Education','GK':'General Knowledge','RHY':'Rhymes & Stories','NUM':'Numbers (Maths Readiness)','VAR':'Hindi Varnamala','PEN':'English Alphabet'}

def periods(cls):  # weekly periods per subject (sum = 36)
    i = CLASSES.index(cls)
    if i < 3: return {'PEN':6,'NUM':6,'VAR':6,'RHY':6,'ART':6,'EVS':6}
    n = i - 2
    if n <= 2: return {'ENG':7,'HIN':6,'MAT':7,'EVS':5,'ART':4,'GK':3,'PE':4}
    if n <= 5: return {'ENG':6,'HIN':5,'MAT':7,'SCI':5,'SST':5,'CMP':3,'PE':3,'ART':2}
    if n <= 8: return {'ENG':6,'HIN':5,'MAT':6,'SCI':6,'SST':5,'SAN':3,'CMP':3,'PE':2}
    return {'ENG':7,'HIN':6,'MAT':7,'SCI':6,'SST':5,'CMP':3,'PE':2}

def sections():
    out = []
    for c in CLASSES:
        out.append((c,'A'))
        if c in TWO_SECTION: out.append((c,'B'))
    return out

def teacher_plan():
    P = {}  # (class, section, subjcode) -> teacher key
    for k,c in zip(('PP1','PP2','PP3'), CLASSES[:3]):
        for sc in periods(c): P[(c,'A',sc)] = k
    S = {n: [x for x in sections() if x[0] == f'Class {n}'] for n in range(1,11)}
    for key, ns, subs in [('E1',(1,2,3,4),['ENG']),('E2',(5,6,7),['ENG']),('E3',(8,9,10),['ENG']),
        ('H1',(1,2,3,4),['HIN']),('H2',(5,6,7),['HIN','SAN']),('H3',(8,9,10),['HIN','SAN']),
        ('M1',(1,2,3,4),['MAT']),('M2',(5,6,7),['MAT']),('M3',(8,9,10),['MAT']),
        ('S1',(1,2,3,4),['SCI','EVS']),('S2',(5,6,7,8),['SCI','EVS']),('S3',(9,10),['SCI']),
        ('T1',(3,4,5,6),['SST']),('T2',(7,8,9,10),['SST']),('C1',tuple(range(1,11)),['CMP']),
        ('A1',tuple(range(1,11)),['ART','GK']),('P1',(1,2,3,4,5),['PE']),('P2',(6,7,8,9,10),['PE'])]:
        for n in ns:
            for (c,sn) in S[n]:
                for sc in subs:
                    if sc in periods(c) and (c,sn,sc) not in P: P[(c,sn,sc)] = key
    return P

TEACHER_KEYS = ['PP1','PP2','PP3','E1','E2','E3','H1','H2','H3','M1','M2','M3','S1','S2','S3','T1','T2','C1','A1','P1','P2']
TDESC = {'PP1':'Nursery','PP2':'LKG','PP3':'UKG','E1':'English','E2':'English','E3':'English','H1':'Hindi','H2':'Hindi','H3':'Hindi','M1':'Maths','M2':'Maths','M3':'Maths','S1':'Science','S2':'Science','S3':'Science','T1':'Social Studies','T2':'Social Studies','C1':'Computer','A1':'Art','P1':'Physical Education','P2':'Physical Education'}
QUAL = {'PP':'NTT, B.Ed','E':'M.A. English, B.Ed','H':'M.A. Hindi, B.Ed','M':'M.Sc. Mathematics, B.Ed','S':'M.Sc., B.Ed','T':'M.A. History, B.Ed','C':'MCA, B.Ed','A':'BFA, B.Ed','P':'B.P.Ed'}

def schedule(plan, seed):
    secs = sections(); rnd = random.Random(seed)
    for attempt in range(3000):
        rem = {s: dict(periods(s[0])) for s in secs}
        grid = {}; ok = True
        for p in range(1,7):
            for d in range(1,7):
                cand = {}; trem = {}
                for s in secs:
                    for sc, n in rem[s].items(): trem[plan[(s[0],s[1],sc)]] = trem.get(plan[(s[0],s[1],sc)],0) + n
                for s in secs:
                    lst = []
                    for sc, n in rem[s].items():
                        if n <= 0: continue
                        today = sum(1 for pp in range(1,p) if grid.get((s,d,pp)) == sc)
                        left = 36 - ((p-1)*6 + d - 1); lst.append((100*(n >= left) + n - 2.5*today + 0.6*trem[plan[(s[0],s[1],sc)]] + rnd.random()*1.5, sc))
                    lst.sort(reverse=True); cand[s] = [sc for _, sc in lst]
                match = {}  # teacher -> (section, subj)
                def aug(s, seen):
                    for sc in cand[s]:
                        t = plan[(s[0],s[1],sc)]
                        if t in seen: continue
                        seen.add(t)
                        if t not in match or aug(match[t][0], seen):
                            match[t] = (s, sc); return True
                    return False
                order = secs[:]; rnd.shuffle(order)
                if not all(aug(s, set()) for s in order): ok = False; break
                for t,(s,sc) in match.items(): grid[(s,d,p)] = sc; rem[s][sc] -= 1
            if not ok: break
        if ok: return grid
    raise RuntimeError('timetable not solvable')

def setup(key):
    cfg = SCHOOLS[key]; rnd = random.Random(cfg['seed'])
    stf = f'state_{key}.json'
    st = json.load(open(stf)) if os.path.exists(stf) else {}
    sch = json.load(open('state_schools.json'))[cfg['name']]
    # first login forces a password change; the admin changes it once so they can operate
    try: s = Sess(sch['loginId'], cfg['pw'])
    except RuntimeError:
        s = Sess(sch['loginId'], sch['password']); s('POST','/Auth/change-password',{'currentPassword':sch['password'],'newPassword':cfg['pw']}); s = Sess(sch['loginId'], cfg['pw'])
    st['schoolId'] = sch['loginId']; st['adminPassword'] = cfg['pw']
    def save(): json.dump(st, open(stf,'w'), indent=1)
    def existing(path, key_): return {x[key_]: x for x in s('GET', path)[1]['data'] or []}

    # 2. academic year
    ays = s('GET','/AcademicYear')[1]['data']
    ay = next((a for a in ays if a['yearName']=='2026-27'), None) or s('POST','/AcademicYear',{'yearName':'2026-27','startDate':'2026-04-01','endDate':'2027-03-31'})[1]['data']
    if not ay['isCurrent']: ay = s('POST',f'/AcademicYear/{ay["academicYearId"]}/set-current')[1]['data']
    st['ay'] = ay['academicYearId']; print('year', ay['academicYearId'], ay['isCurrent'])
    # 3. classes + sections
    ex = existing('/Classes','className'); st['classes'] = {}
    for i,(c,code) in enumerate(zip(CLASSES,CCODE),1):
        x = ex.get(c) or s('POST','/Classes',{'className':c,'classCode':code,'sequenceOrder':i})[1]['data']; st['classes'][c] = x['classId']
    exs = {(x['className'],x['sectionName']): x for x in s('GET','/Sections')[1]['data'] or []}; st['sections'] = {}
    for (c,sn) in sections():
        x = exs.get((c,sn)) or s('POST','/Sections',{'classId':st['classes'][c],'sectionName':sn,'maxStrength':40})[1]['data']; st['sections'][f'{c}|{sn}'] = x['sectionId']
    save(); print('classes', len(st['classes']), 'sections', len(st['sections']))
    # 4. subjects + class-subject mapping
    ex = existing('/Subjects','subjectName'); st['subjects'] = {}
    for code,name in SUBJ.items():
        x = ex.get(name) or s('POST','/Subjects',{'subjectName':name,'subjectCode':code,'description':f'{name} curriculum'})[1]['data']; st['subjects'][code] = x['subjectId']
    for c in CLASSES:
        s('POST','/ClassSubjects/bulk',{'classId':st['classes'][c],'subjectIds':[st['subjects'][k] for k in periods(c)]})
    save(); print('subjects', len(st['subjects']))
    # 5. departments, designations, admission types
    DEPT = ['Teaching','Administration','Accounts & Finance','Admissions','Library & Support']
    ex = existing('/EmployeeDepartments','departmentName'); st['dept'] = {}
    for d in DEPT: st['dept'][d] = (ex.get(d) or s('POST','/EmployeeDepartments',{'departmentName':d,'description':f'{d} department'})[1]['data'])['departmentId']
    DES = ['Principal','Vice Principal','Pre-Primary Teacher','Primary Teacher (PRT)','Trained Graduate Teacher (TGT)','Post Graduate Teacher (PGT)','Accountant','Admission Officer','Librarian','Office Clerk']
    ex = existing('/EmployeeDesignations','designationName'); st['desig'] = {}
    for d in DES: st['desig'][d] = (ex.get(d) or s('POST','/EmployeeDesignations',{'designationName':d,'description':d})[1]['data'])['designationId']
    ex = existing('/AdmissionTypes','typeName'); st['admtype'] = {}
    for n,dsc in [('Existing Student','Students already studying at the school'),('New Admission','Fresh admission through the application pipeline'),('Transfer Admission','Admission on transfer certificate')]:
        st['admtype'][n] = (ex.get(n) or s('POST','/AdmissionTypes',{'typeName':n,'description':dsc})[1]['data'])['admissionTypeId']
    save()
    # 6. employees + logins
    lk = s('GET','/Employee/lookups')[1]['data']; roles = {r['roleName']: r['roleId'] for r in lk['assignableRoles']}; print('roles', list(roles))
    emp_ex = {x['fullName']: x for x in s('GET','/Employee?pageSize=100')[1]['data']['items']}
    base = 9100000000 + cfg['seed']*1000; mob = iter(range(base, base+900))
    names = list(cfg['staff'])
    people = []
    for k in TEACHER_KEYS:
        nm = names.pop(0)
        desig = 'Pre-Primary Teacher' if k.startswith('PP') else ('Primary Teacher (PRT)' if k in ('E1','H1','M1','S1','A1','P1') else ('Trained Graduate Teacher (TGT)' if k in ('E2','H2','M2','S2','T1','C1','P2') else 'Post Graduate Teacher (PGT)'))
        people.append(dict(key=k, name=nm, type='Teaching', dept='Teaching', desig=desig, role='Teacher', qual=QUAL['PP' if k.startswith('PP') else k[0]] + f' ({TDESC[k]})'))
    people += [dict(key='PRIN',name=cfg['principal'],type='Non-Teaching',dept='Administration',desig='Principal',role='Principal',qual='M.Ed, Ph.D'),
      dict(key='ACC',name=cfg['accountant'],type='Non-Teaching',dept='Accounts & Finance',desig='Accountant',role='Accountant',qual='M.Com'),
      dict(key='ADM',name=cfg['admission'],type='Non-Teaching',dept='Admissions',desig='Admission Officer',role='Admission Staff',qual='B.A., Diploma in Office Management'),
      dict(key='LIB',name=cfg['librarian'],type='Non-Teaching',dept='Library & Support',desig='Librarian',role=None,qual='B.Lib.Sc'),
      dict(key='CLK',name=cfg['clerk'],type='Non-Teaching',dept='Library & Support',desig='Office Clerk',role=None,qual='B.Com')]
    st['employees'] = st.get('employees', {}); st['logins'] = st.get('logins', {})
    for p in people:
        fn, ln = p['name'].split(' ', 1)
        e = emp_ex.get(p['name'])
        if not e:
            body = dict(designationId=st['desig'][p['desig']], departmentId=st['dept'][p['dept']], employmentStatusId=1, employeeType=p['type'], firstName=fn, lastName=ln,
              genderId=2 if fn in FEM else 1, bloodGroupId=rnd.choice([1,3,5,7]), dateOfBirth=f'{rnd.randint(1972,1996)}-{rnd.randint(1,12):02d}-{rnd.randint(1,28):02d}',
              mobileNumber=str(next(mob)), address=f'{rnd.randint(1,120)} Staff Colony, Campus Road', dateOfJoining=f'{rnd.randint(2015,2025)}-0{rnd.randint(4,7)}-01', qualification=p['qual'])
            e = s('POST','/Employee',body)[1]['data']
        eid = e['employeeId']; st['employees'][p['key']] = {'employeeId': eid, 'name': p['name'], 'type': p['type'], 'designation': p['desig']}
        if p['role'] and not e.get('loginId') and eid not in st['logins']:
            r = s('POST',f'/Employee/{eid}/create-login',{'roleIds':[roles[p['role']]]})[1]['data']
            st['logins'][eid] = {'name': p['name'], 'role': p['role'], 'loginId': r['orgUserId'], 'password': r['temporaryPassword'], 'kind': 'employee'}
        save()
    print('employees', len(st['employees']), 'logins', len(st['logins']))
    # 7. class teachers
    plan = teacher_plan(); tid = lambda k: st['employees'][k]['employeeId']
    taken = set(); st['classTeacher'] = {}
    for (c,sn) in sections():
        if c in CLASSES[:3]: k = {'Nursery':'PP1','LKG':'PP2','UKG':'PP3'}[c]
        else:
            load = {}
            for sc,n in periods(c).items(): load[plan[(c,sn,sc)]] = load.get(plan[(c,sn,sc)],0)+n
            k = next(t for t,_ in sorted(load.items(), key=lambda kv: -kv[1]) if t not in taken and t not in ('P1','P2','C1','A1'))
        taken.add(k); st['classTeacher'][f'{c}|{sn}'] = k
        s('POST','/ClassTeacher/assign',{'academicYearId':st['ay'],'sectionId':st['sections'][f'{c}|{sn}'],'employeeId':tid(k)})
    save(); print('class teachers', len(st['classTeacher']))
    # 8. periods + timetable
    pdef = [(1,'08:30','09:10'),(2,'09:10','09:50'),(3,'09:50','10:30'),(4,'10:50','11:30'),(5,'11:30','12:10'),(6,'12:10','12:50')]
    ex = {x['periodNumber']: x for x in s('GET','/Period')[1]['data']}; st['periods'] = {}
    for n,a,b in pdef: st['periods'][n] = (ex.get(n) or s('POST','/Period',{'periodNumber':n,'startTime':a,'endTime':b})[1]['data'])['periodId']
    grid = schedule(plan, cfg['seed']); cnt = 0
    for (sec,d,p),sc in sorted(grid.items(), key=lambda kv: (kv[0][0], kv[0][1], kv[0][2])):
        s('POST','/Timetable/assign',{'academicYearId':st['ay'],'sectionId':st['sections'][f'{sec[0]}|{sec[1]}'],'dayOfWeek':d,'periodId':st['periods'][p],'subjectId':st['subjects'][sc],'employeeId':tid(plan[(sec[0],sec[1],sc)]),'roomNo':f'Room {CLASSES.index(sec[0])+1}{sec[1]}'}); cnt += 1
    st['teacherOf'] = {f'{c}|{sn}|{sc}': k for (c,sn,sc),k in plan.items()}
    save(); print('timetable entries', cnt)
    # 9. fee setup
    FT = [('Tuition Fee','TUITION',False),('Admission Fee','ADMISSION',False),('Annual Charges','ANNUAL',False),('Exam Fee','EXAM',False),('Library Fee','LIBRARY',False),('Transport Fee','TRANSPORT',True),('Computer Lab Fee','COMPLAB',False)]
    ex = existing('/FeeType','feeTypeCode'); st['feeTypes'] = {}
    for n,code,opt in FT: st['feeTypes'][code] = (ex.get(code) or s('POST','/FeeType',{'feeTypeName':n,'feeTypeCode':code,'description':n,'isOptional':opt})[1]['data'])['feeTypeId']
    freq = {f['frequencyCode']: f['frequencyId'] for f in s('GET','/FeeStructure/frequencies')[1]['data']}
    exs = {x['classId']: x for x in s('GET','/FeeStructure')[1]['data'] or []}; st['feeStructures'] = {}
    for i,c in enumerate(CLASSES):
        tui = [1800,1900,2000,2200,2300,2400,2500,2600,2800,3000,3200,3600,3800][i]; adm = 4000 + 500*(i//3); ann = 3000 + 250*i
        det = [dict(feeTypeId=st['feeTypes']['TUITION'],frequencyId=freq['MONTHLY'],amount=tui,dueDay=10),
               dict(feeTypeId=st['feeTypes']['ADMISSION'],frequencyId=freq['ONE_TIME'],amount=adm,dueDay=5),
               dict(feeTypeId=st['feeTypes']['ANNUAL'],frequencyId=freq['ANNUAL'],amount=ann,dueDay=15),
               dict(feeTypeId=st['feeTypes']['EXAM'],frequencyId=freq['HALF_YEARLY'],amount=800+100*(i//2),dueDay=20),
               dict(feeTypeId=st['feeTypes']['LIBRARY'],frequencyId=freq['ANNUAL'],amount=500,dueDay=15),
               dict(feeTypeId=st['feeTypes']['TRANSPORT'],frequencyId=freq['MONTHLY'],amount=1500,dueDay=10)]
        if i >= 5: det.append(dict(feeTypeId=st['feeTypes']['COMPLAB'],frequencyId=freq['QUARTERLY'],amount=600,dueDay=12))
        x = exs.get(st['classes'][c]) or s('POST','/FeeStructure',{'academicYearId':st['ay'],'classId':st['classes'][c],'structureName':f'{c} Fee Structure 2026-27','details':det})[1]['data']
        st['feeStructures'][c] = x['feeStructureId']
    ex = existing('/FeeConcession','concessionCode'); st['concessions'] = {}
    for n,code,t,v,ft in [('Sibling Discount','SIBLING','PERCENTAGE',10,'TUITION'),('Staff Ward Concession','STAFFWARD','PERCENTAGE',50,'TUITION'),('Merit Scholarship','MERIT','FIXED',5000,'ANNUAL')]:
        st['concessions'][code] = (ex.get(code) or s('POST','/FeeConcession',{'concessionName':n,'concessionCode':code,'concessionType':t,'value':v,'applicableFeeTypeId':st['feeTypes'][ft]})[1]['data'])['concessionId']
    save(); print('fees done')

if __name__ == '__main__': setup(sys.argv[1])
