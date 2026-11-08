
from pathlib import Path
from zipfile import ZipFile
import hashlib
import json
import re
import sys
import numpy as np

repo=Path(sys.argv[1])
output=Path(sys.argv[2])
src=repo/'src/OpticalSystemDesign.App/Assets/SequentialExamples/Miscellaneous/Toroid.zmx'
source=src.read_bytes()
encoding='utf-16' if source[:2]==b'\xff\xfe' else 'utf-8-sig'
head,*sections=re.split(r'^SURF \d+\s*$', source.decode(encoding), flags=re.M)
def read(section,name):
 return float(re.search(r'^\s*'+name+r'\s+(\S+)',section,re.M).group(1))
assert all('TYPE TOROIDAL' in sections[i] for i in (2,3))
assert all(read(sections[i],f'PARM {j}')==0 for i in (2,3) for j in (0,2,3,4,5,6,7,8))
assert read(sections[1],'PARM 5')==-read(sections[4],'PARM 5')==45
assert read(sections[1],'DISZ')==read(sections[3],'DISZ')==0
assert 'GLAS BK7 ' in sections[2] and 'ROPD 2' in head
pupil=read(head,'ENPD')/2
wavelength=read(head,'WAVM 1')
angles=np.deg2rad(read(sections[1],'PARM 5'))
c,s=np.cos(angles),np.sin(angles)
local=np.array([[c,-s,0],[s,c,0],[0,0,1]])
r1,r2=[1/read(sections[i],'CURV') for i in (2,3)]
t1,t2=[read(sections[i],'PARM 1') for i in (2,3)]
thickness=read(sections[2],'DISZ')
z_image=thickness+read(sections[4],'DISZ')
B=np.array([1.03961212,.231792344,1.01046945])
L=np.array([.00600069867,.0200179144,103.560653])
n=np.sqrt(1+np.sum(B*wavelength*wavelength/(wavelength*wavelength-L)))

def dot(a,b):
 return np.sum(a*b,axis=-1)

def sag(x,y,meridian_radius,rotation_radius):
 meridian_root=np.sqrt(meridian_radius**2-y*y)
 meridian_sag=y*y/(meridian_radius+meridian_root)
 rotation_offset=rotation_radius-meridian_sag
 rotation_root=np.sqrt(rotation_offset**2-x*x)
 return (meridian_sag+x*x/(rotation_offset+rotation_root),x/rotation_root,
         rotation_offset*y/(meridian_root*rotation_root))

def refract(d,dx,dy,ratio):
 normal=np.column_stack((-dx,-dy,np.ones(dx.size)))
 normal/=np.linalg.norm(normal,axis=1)[:,None]
 cosine=dot(normal,d)
 transmitted=np.sqrt(1-ratio**2*(1-cosine*cosine))
 return ratio*d+(transmitted-ratio*cosine)[:,None]*normal

def trace(px,py):
 launch=np.column_stack((px*pupil,py*pupil,np.zeros(px.size)))@local
 z,dx,dy=sag(launch[:,0],launch[:,1],r1,t1)
 front=launch.copy()
 front[:,2]=z
 incoming=np.tile([0.,0.,1.],(px.size,1))
 glass=refract(incoming,dx,dy,1/n)
 distance=(thickness-z)/glass[:,2]
 for _ in range(8):
  back=front+distance[:,None]*glass
  sz,dx,dy=sag(back[:,0],back[:,1],r2,t2)
  error=back[:,2]-thickness-sz
  if np.max(abs(error))<1e-13: break
  distance-=error/(glass[:,2]-dx*glass[:,0]-dy*glass[:,1])
 else: raise ValueError('rear intercept')
 assert np.max(np.hypot(front[:,0],front[:,1]))<=read(sections[2],'DIAM')
 assert np.max(np.hypot(back[:,0],back[:,1]))<=read(sections[3],'DIAM')
 air=refract(glass,dx,dy,n)@local.T
 air_distance=(z_image-back[:,2])/air[:,2]
 image=back@local.T+air_distance[:,None]*air
 return air,image,z+n*distance+air_distance

chief=trace(np.zeros(1),np.zeros(1))
chief_path=chief[2][0]
assert np.array_equal(chief[0][0],[0,0,1])
def correction_data(px,py):
 directions,points,paths=trace(px,py)
 offsets=points-chief[1][0]
 cross=np.cross(offsets,directions)
 return chief_path-paths+dot(offsets,directions),dot(cross,cross)

def phase(px,py,radius):
 line,off=correction_data(px,py)
 return (line-off/(radius+np.sqrt(radius**2-off)))/(wavelength*1e-3)

archive=repo/'validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip'
with ZipFile(archive) as z:
 wave_member=next(path for path in z.namelist() if '/1364-' in path and path.endswith('/native/data.json'))
 ref=z.read(wave_member)
 request_members={}
 for idx in (1365,1367,1369):
  member=next(path for path in z.namelist() if f'/{idx}-' in path and path.endswith('/workbench/request.json'))
  payload=z.read(member)
  request_members[idx]={'member':member,'sha256':hashlib.sha256(payload).hexdigest(),'canonical':json.loads(payload)['request']}
 phase_grid=json.loads(ref)['dataGrids'][0]
 sources=json.loads(z.read('run/sources.json'))
 record=next(row for row in sources if row['source']['id']=='Miscellaneous/Toroid.zmx')
 assert hashlib.sha256(source).hexdigest()==record['actualSha256']
 assert request_members[1365]['canonical']['imageDeltaMicrometers']==.25
 assert request_members[1367]['canonical']['maximumFrequency']==50
 assert request_members[1369]['canonical']['workbenchSettings']['GraphScaleMicrometers']=='0'
v=np.asarray(phase_grid['values'],dtype=float)
a=(np.arange(64)-32)/31
xx,yy=np.meshgrid(a,a)
mask=np.isfinite(v)
def sphere(j,i):
 line,off=correction_data(np.array([a[i]]),np.array([a[j]]))
 offset=(v[j,i]-v[32,32])*wavelength*1e-3-line[0]
 assert offset<0
 return -(off[0]+offset**2)/(2*offset)
radius=sphere(1,32)
native_error=phase(xx[mask],yy[mask],radius)-(v[mask]-v[32,32])
print('source',record['actualSha256'],'radii',radius,sphere(32,1),'native nodes',mask.sum(),'phase residual',np.max(np.abs(native_error)))
assert np.max(abs(native_error))<1e-6
dirs=trace(np.array([1.,-1.,0.,0.]),np.array([0.,0.,1.,-1.]))[0]
na=np.linalg.norm(np.cross(dirs,chief[0][0]),axis=1)
F=.5/np.sqrt(np.mean(na*na))
print('working F',F,'edge numerical apertures',na)

def axis_witness(count,step,radius):
 # Probe the Cartesian region containing the rotated physical meridian.
 # Retain only pairs and stencil nodes strictly inside the pupil.
 candidates=np.arange(round(.63/step),round(.70/step),max(1,round(.002/step)))
 ix,iy=np.meshgrid(candidates,candidates)
 ix,iy=ix.ravel(),iy.ravel()
 def filtered(i,j):
  x,y=i*step,j*step
  assert np.max(x*x+y*y)<1
  raw=phase(x,y,radius)
  smooth=((i+j)%2)==0
  if np.any(smooth):
   a,b=x[smooth],y[smooth]
   raw[smooth]=(phase(a-step,b,radius)+phase(a+step,b,radius)+
                phase(a,b-step,radius)+phase(a,b+step,radius))/4
  return raw
 deltas=abs(filtered(ix+1,iy)-filtered(ix,iy))
 k=np.argmax(deltas)
 return {'pupilCount':count,'normalizedStep':step,'node':[(ix[k]*step),(iy[k]*step)],
         'neighbor':[(ix[k]+1)*step,iy[k]*step],'adjacentFilteredPhaseWaves':deltas[k]}
psf=axis_witness(8192,wavelength*F/(8192*.25),radius)
mtf=axis_witness(6144,1/(6144/2-1),radius)
profile=axis_witness(2048,2.5/2048,radius)
ordinary_profile=axis_witness(16384,np.sqrt(16384/32)/(16384/2-1),radius)
nominal_radius=166.42133811423807
nominal={'psf':axis_witness(8192,wavelength*F/(8192*.25),nominal_radius),
 'mtf':axis_witness(6144,1/(6144/2-1),nominal_radius),
 'denseProfile':axis_witness(2048,2.5/2048,nominal_radius)}
h=1e-6
# Estimate the local finite physical density from one fixed interior probe.
x,y=.69,.69
partial=abs((phase(np.array([x+h]),np.array([y]),radius)-phase(np.array([x-h]),np.array([y]),radius))/(2*h))[0]
print('witness',psf,mtf,profile,nominal,'local derivative',partial,'projected requirements',2*partial*wavelength*F/.25,4*partial+2)
output.write_text(json.dumps({'lens':record['source']['id'],'sourceSha256':record['actualSha256'],
 'nativeWavefrontMember':wave_member,'nativeWavefrontSha256':hashlib.sha256(ref).hexdigest(),
 'requests':request_members,'referenceSphereMm':radius,'nativePhaseNodes':int(mask.sum()),
 'maxNativePhaseErrorWaves':float(np.max(np.abs(native_error))),'workingFNumber':F,
 'psfCurrentCap':psf,'mtfCurrentCap':mtf,'denseProfileCurrentCap':profile,'ordinaryProfileCurrentCap':ordinary_profile,
 'nominalSphereControl':{'radiusMm':nominal_radius,'witnesses':nominal},'probe':{'x':x,'y':y,'wavesPerUnitPupilX':partial,
 'projectedPsfNominalSampling':2*partial*wavelength*F/.25,
 'projectedUniformFullPupilSampling':4*partial+2}},indent=2)+'\n')

