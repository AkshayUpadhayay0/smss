from common import *
import json
c,j=call('POST','/Auth/login',None,{'loginId':'SUAD001','password':os.environ['SA_PW']}); T=j['data']['token']
SCH=[
 dict(schoolName='Little Angels School',schoolShortName='LAS',schoolCode='LAS-DDN-2026',schoolTypeId=1,schoolLevelId=3,boardTypeId=1,schoolEstablishYear=2005,countryId=91,stateId=5,districtId=49,addressLine1='12 Rajpur Road',addressLine2='Near Clock Tower',pincode='248001',email='office@littleangels-demo.in',mobileNumber='9876500101',website='https://www.littleangels-demo.in',
   contacts=[dict(contactType='Principal',contactName='Mrs. Kavita Sharma',designation='Principal',email='principal@littleangels-demo.in',mobileNumber='9876500102',isPrimary=True),dict(contactType='Accountant',contactName='Mr. Rajesh Bisht',designation='Accountant',mobileNumber='9876500103',isPrimary=False)]),
 dict(schoolName='Green Hills School',schoolShortName='GHS',schoolCode='GHS-RKE-2026',schoolTypeId=1,schoolLevelId=3,boardTypeId=1,schoolEstablishYear=1998,countryId=91,stateId=5,districtId=50,cityId=390,addressLine1='45 Civil Lines',addressLine2='Roorkee Road',pincode='247667',email='office@greenhills-demo.in',mobileNumber='9876500201',website='https://www.greenhills-demo.in',
   contacts=[dict(contactType='Principal',contactName='Dr. Anil Verma',designation='Principal',email='principal@greenhills-demo.in',mobileNumber='9876500202',isPrimary=True)]),
]
out={}
for s in SCH:
    c,j=call('POST','/SchoolRegistration/register',T,s); print(c,j['message'])
    if c in (200,201):
        d=j['data']; out[s['schoolName']]={'schoolId':d['school']['schoolId'],'loginId':d['loginId'],'password':d['temporaryPassword']}
        print(out[s['schoolName']]['schoolId'],d['loginId'],d['emailSent'])
    else: print(j)
json.dump(out,open('state_schools.json','w'),indent=1)
