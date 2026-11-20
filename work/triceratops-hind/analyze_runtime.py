import csv, json, statistics, math
from pathlib import Path
from collections import defaultdict
root=Path('/Users/victorazevedo/projetos/vale-mesozoico-vr/work')
out={}
for run in ['hind-before','hind-after','hind-refined','posture','cadence']:
    groups=defaultdict(list)
    for r in csv.DictReader((root/'triceratops-walk'/run/'motion.csv').open()): groups[(r['actor'],r['leg'])].append(r)
    result=[]
    for (actor,leg),rows in sorted(groups.items()):
        if int(leg)<2:continue
        bend=[float(r['bendForward']) for r in rows];knee=[float(r['kneeAngle']) for r in rows]
        active=[float(r['supportError']) for r in rows if r['swing']=='0']
        values={'actor':actor,'leg':leg,'poleFlips':sum(a*b<0 for a,b in zip(bend,bend[1:])), 'wrongPoleFrames':sum(v<0 for v in bend),'bendForwardMin':min(bend),'kneeAngleMin':min(knee),'kneeAngleMax':max(knee),'kneeAngleMean':statistics.mean(knee),'kneeMaxDelta':max(abs(a-b) for a,b in zip(knee,knee[1:])),'nearStraight170':sum(v>=170 for v in knee),'nearStraight175':sum(v>=175 for v in knee),'maxSupportError':max(active),'maxStanceSlip':max(float(r['stanceSlip']) for r in rows),'stanceReplants':sum(int(r['stanceReplant']) for r in rows)}
        result.append(values)
        print(run,actor,leg,'flips',values['poleFlips'],'knee',round(min(knee),2),round(max(knee),2),'maxDelta',round(values['kneeMaxDelta'],2),'straight170',values['nearStraight170'],'straight175',values['nearStraight175'],'support',round(values['maxSupportError'],5),'slip',round(values['maxStanceSlip'],5))
    out[run]=result
(root/'triceratops-hind/runtime-comparison.json').write_text(json.dumps(out,indent=2))
