"""Builds the throwaway ZZ demo school in smss-db-dev through the scratch API (normal endpoints wherever they exist) and records every created id in session.json."""
import datetime, json, sys
import api, devdb

st = api.session()
today = datetime.date.today()
d = lambda n: (today + datetime.timedelta(days=n)).isoformat()
ADMIN_PW, PRINCIPAL_PW, SUPER_PW = 'ZZ-Admin#2026a', 'ZZ-Princ#2026a', 'ZZ-Super#2026a'
DRIVER_PW = {'D1': 'ZZ-Drv1#2026a', 'D2': 'ZZ-Drv2#2026a'}

# --- super admin (bootstrap endpoint, enabled only on this scratch process) + the one global row dev needs for registration
r = api.call('POST', '/api/SystemSetup/bootstrap-super-admin', headers={'X-Setup-Key': st['setupKey']},
             body={'displayName': 'ZZ Throwaway Super Admin', 'email': 'zz-super@example.invalid', 'mobileNumber': '9000000001', 'password': SUPER_PW})
sa = api.ok(r, 'bootstrap super admin')
st['superAdmin'] = {'loginId': sa['orgUserId'], 'password': SUPER_PW, 'userId': sa['userId']}
st['pricingId'] = int(devdb.psql("insert into tb_subscription_pricing (rate_per_student_monthly, yearly_discount_percent, trial_days) values (10, 0, 30) returning pricing_id").split()[0])
api.save(st)
stok, _ = api.login(sa['orgUserId'], SUPER_PW)

# --- school (register creates the School Admin login)
reg = api.ok(api.call('POST', '/api/SchoolRegistration/register', stok, {
    'schoolCode': 'ZZDEMO01', 'schoolName': 'ZZ Demo Transport School', 'schoolShortName': 'ZZ Demo', 'email': 'zz-school@example.invalid', 'mobileNumber': '9000000002',
    'addressLine1': 'ZZ Test Road', 'pincode': '226001',
    'contacts': [{'contactType': 'School Owner', 'contactName': 'ZZ Owner', 'email': 'zz-owner@example.invalid', 'mobileNumber': '9000000003', 'isPrimary': True}]}), 'register school')
st['schoolId'] = reg['school']['schoolId']
tmp = reg['temporaryPassword']
tok, _ = api.login(st['schoolId'], tmp)
api.ok(api.call('POST', '/api/Auth/change-password', tok, {'currentPassword': tmp, 'newPassword': ADMIN_PW}), 'admin password', (200,))
tok, _ = api.login(st['schoolId'], ADMIN_PW)
st['admin'] = {'loginId': st['schoolId'], 'password': ADMIN_PW}
api.save(st)

# --- Principal login (employee + create-login with the Principal role)
des = api.ok(api.call('POST', '/api/EmployeeDesignations', tok, {'designationName': 'ZZ Principal'}), 'designation')
dep = api.ok(api.call('POST', '/api/EmployeeDepartments', tok, {'departmentName': 'ZZ Administration'}), 'department')
st['designationId'], st['departmentId'] = des['designationId'], dep['departmentId']
emp = api.ok(api.call('POST', '/api/Employee', tok, {'designationId': des['designationId'], 'departmentId': dep['departmentId'], 'employeeType': 'Non-Teaching', 'firstName': 'ZZ',
        'lastName': 'Principal', 'dateOfBirth': '1980-04-12', 'mobileNumber': '9000000021', 'email': 'zz-principal@example.invalid', 'address': 'ZZ Staff Quarters', 'dateOfJoining': '2020-04-01'}), 'employee')
st['employeeId'] = emp['employeeId']
lk = api.ok(api.call('GET', '/api/Employee/lookups', tok), 'employee lookups')
role = next(x['roleId'] for x in lk['assignableRoles'] if x['roleName'] == 'Principal')
login = api.ok(api.call('POST', f"/api/Employee/{emp['employeeId']}/create-login", tok, {'roleIds': [role]}), 'principal login')
ptok, _ = api.login(login['orgUserId'], login['temporaryPassword'])
api.ok(api.call('POST', '/api/Auth/change-password', ptok, {'currentPassword': login['temporaryPassword'], 'newPassword': PRINCIPAL_PW}), 'principal password', (200,))
st['principal'] = {'loginId': login['orgUserId'], 'password': PRINCIPAL_PW}
api.save(st)

# --- transport data
areas = {}
for key, name, km, fee in [('A1', 'ZZ Civil Lines', 6.5, 800), ('A2', 'ZZ Ring Road', 12, 1200), ('A3', 'ZZ Old City', 3, 500)]:
    areas[key] = api.ok(api.call('POST', '/api/TransportAreas', tok, {'areaName': name, 'distanceKm': km, 'transportFee': fee}), 'area ' + name)['areaId']
lk = api.ok(api.call('GET', '/api/Vehicles/lookups', tok), 'vehicle lookups')
vt = {t['name']: t['id'] for t in lk['vehicleTypes']}
ow = {o['name']: o['id'] for o in lk['ownerships']}
v1 = api.ok(api.call('POST', '/api/Vehicles', tok, {'vehicleLabel': 'ZZ Bus 1', 'registrationNumber': 'ZZ01AB1001', 'vehicleTypeId': vt['Bus'], 'ownershipId': ow['Own'], 'modelYear': 2019,
        'seatingCapacity': 40, 'lastServiceDate': d(-45), 'pucExpiryDate': d(-24), 'insuranceExpiryDate': d(200)}), 'vehicle 1')
v2 = api.ok(api.call('POST', '/api/Vehicles', tok, {'vehicleLabel': 'ZZ Van 2', 'registrationNumber': 'ZZ02CD2002', 'vehicleTypeId': vt['Van'], 'ownershipId': ow['Rented'], 'modelYear': 2022,
        'seatingCapacity': 14, 'ownerName': 'ZZ Sharma Travels', 'ownerContact': '9000000031', 'lastServiceDate': d(-20), 'pucExpiryDate': d(140), 'insuranceExpiryDate': d(10)}), 'vehicle 2')
dr1 = api.ok(api.call('POST', '/api/Drivers', tok, {'fullName': 'ZZ Ramesh Kumar', 'mobileNumber': '9000000011', 'email': 'zz-ramesh@example.invalid', 'address': '12 ZZ Main Road',
        'dlNumber': 'ZZDL-2020-0001', 'dlValidUpto': d(1900), 'vehicleId': v1['vehicleId'], 'areaIds': [areas['A1'], areas['A2']]}), 'driver 1')
dr2 = api.ok(api.call('POST', '/api/Drivers', tok, {'fullName': 'ZZ Suresh Yadav', 'mobileNumber': '9000000012', 'address': '7 ZZ Market Lane',
        'dlNumber': 'ZZDL-2019-0002', 'dlValidUpto': d(22), 'vehicleId': v2['vehicleId'], 'areaIds': [areas['A2'], areas['A3']]}), 'driver 2')
st['areas'], st['vehicles'], st['drivers'] = areas, {'V1': v1['vehicleId'], 'V2': v2['vehicleId']}, {'D1': dr1['driverId'], 'D2': dr2['driverId']}
api.save(st)

# --- driver logins (create-login, then a known password through change-password)
st['driverLogins'] = {}
for key in ('D1', 'D2'):
    lg = api.ok(api.call('POST', f"/api/Drivers/{st['drivers'][key]}/create-login", tok), 'driver login ' + key)
    dtok, du = api.login(lg['orgUserId'], lg['temporaryPassword'])
    api.ok(api.call('POST', '/api/Auth/change-password', dtok, {'currentPassword': lg['temporaryPassword'], 'newPassword': DRIVER_PW[key]}), 'driver password ' + key, (200,))
    st['driverLogins'][key] = {'loginId': lg['orgUserId'], 'password': DRIVER_PW[key], 'roles': du['roles']}
api.save(st)
print(json.dumps({k: st[k] for k in ('schoolId', 'admin', 'principal', 'areas', 'vehicles', 'drivers', 'driverLogins')}, indent=1))
