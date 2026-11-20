import runpy
from pathlib import Path
ns = runpy.run_path(str(Path.cwd() / 'Tools/Blender/build_track_mountain_cave.py'))
spline = ns['RideSpline']()
peak = max((spline.frame(i/4000)[0].y, i/4000) for i in range(400,2400))
print('ROUTE', spline.length, 'PEAK', peak, flush=True)
for i in range(35,64):
    p,t,r,u=spline.frame(i/100)
    print(round(i/100,3), tuple(round(v,2) for v in p), 'slope',round(t.y,3), flush=True)
