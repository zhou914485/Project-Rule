#!/usr/bin/env python3
"""Curate CC0 source materials; trim, layer foley, fade and balance gain. No EQ.

Inputs are cached author downloads (--sources), not downloaded by this script.
Run without --install to prepare audition files only. Requires numpy and ffmpeg.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import wave
import zipfile
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'Project-Rule/Assets/_Project/Audio/SFX'
DOC = ROOT / 'Doc/AudioSources'
PREVIEW = ROOT / 'Doc/AudioPreviews/SourceSelection'
RATE = 44100
PACKS = {
    'impact': ('Kenney', 'https://kenney.nl/assets/impact-sounds', 'kenney_impact-sounds.zip'),
    'interface': ('Kenney', 'https://kenney.nl/assets/interface-sounds', 'kenney_interface-sounds.zip'),
    'rpg': ('Kenney', 'https://kenney.nl/assets/rpg-audio', 'kenney_rpg-audio.zip'),
    'rpg80': ('rubberduck', 'https://opengameart.org/content/80-cc0-rpg-sfx', 'rpg80.zip'),
    'scifi': ('Kenney', 'https://kenney.nl/assets/sci-fi-sounds', 'sci-fi_sounds.zip'),
    'listener': ('Listener', 'https://opengameart.org/content/sound-effects-for-platformer', 'platformer-listener.zip'),
    'hits': ('Independent.nu', 'https://opengameart.org/content/37-hitspunches', 'hits.7z'),
    'wood': ('Independent.nu', 'https://opengameart.org/content/35-wooden-crackshitsdestructions', 'wood.7z'),
    'cardboard': ('StarNinjas', 'https://opengameart.org/content/10-impactshield-blocks', 'cardboard.zip'),
}

# A layer specifies the actual source, excerpt, relative gain and placement.
# Deliberately no filtering, frequency boosting or baked pitch changes.
def layer(pack, file, start, end, gain=1, at=0):
    return dict(pack=pack, source=file, start=start, end=end, gain=gain, at=at)

RECIPES = {}
def sound(name, rms, *layers):
    RECIPES[name] = dict(rms=rms, layers=layers)
    return name

jump = sound('Selected/Jump', .10, layer('listener', 'Jump.wav', .047, .45))
land_a = sound('PlayerFeedback/PlayerLand', .095,
    layer('rpg', 'footstep01.ogg', .033, .185, .8), layer('cardboard', 'impact.2.ogg', .141, .235, .26))
land_b = sound('Selected/Land_B', .095,
    layer('rpg', 'footstep02.ogg', .02, .19, .8), layer('cardboard', 'impact.7.ogg', .274, .39, .24))
dash = sound('Selected/Dash', .12, layer('rpg', 'clothBelt.ogg', .49, .655))
light_a = sound('Swishes/SwordLight_A', .13, layer('rpg', 'knifeSlice.ogg', .08, .315))
light_b = sound('Swishes/SwordLight_B', .13, layer('rpg', 'knifeSlice2.ogg', .015, .25))
heavy_a = sound('Swishes/SwordHeavy_A', .15, layer('rpg', 'knifeSlice.ogg', .08, .365))
heavy_b = sound('Swishes/SwordHeavy_B', .15, layer('rpg', 'knifeSlice2.ogg', .015, .30))
hurt_a = sound('PlayerFeedback/PlayerHurt', .14, layer('hits', 'hit12.mp3.flac', .13, .385))
hurt_b = sound('Selected/Hurt_B', .14, layer('hits', 'hit11.mp3.flac', .157, .415))
open_s = sound('Selected/WheelOpen', .09, layer('interface', 'maximize_001.ogg', .025, .258))
preview = sound('Selected/WheelPreview', .075, layer('interface', 'pluck_001.ogg', 0, .10))
confirm = sound('Selected/WheelConfirm', .10, layer('interface', 'confirmation_003.ogg', 0, .322))
cancel = sound('Selected/WheelCancel', .09, layer('interface', 'minimize_001.ogg', 0, .258))
back = sound('Selected/WheelBack', .085, layer('interface', 'back_001.ogg', 0, .061))
error = sound('Selected/WheelError', .09, layer('interface', 'error_003.ogg', .01, .42))
shot = sound('Selected/EnemyShot', .10, layer('scifi', 'laserSmall_002.ogg', 0, .344))
beam = sound('Selected/EnemyBeam', .12, layer('scifi', 'laserLarge_003.ogg', 0, .716))
wood_a = sound('Selected/HitWood_A', .12, layer('wood', 'impactwood14.mp3.flac', .087, .34))
wood_b = sound('Selected/HitWood_B', .12, layer('rpg80', 'item_wood_03.ogg', .01, .26))
metal_a = sound('Selected/HitMetal_A', .12, layer('impact', 'impactMetal_light_002.ogg', 0, .233))
metal_b = sound('Selected/HitMetal_B', .12, layer('impact', 'impactMetal_light_004.ogg', 0, .21))
soft_a = sound('Selected/HitSoft_A', .12, layer('cardboard', 'impact.2.ogg', .141, .25))
soft_b = sound('Selected/HitSoft_B', .12, layer('cardboard', 'impact.7.ogg', .274, .40))
deathwood = sound('Selected/DeathWood', .14, layer('wood', 'crack05.mp3.flac', .11, .86))
deathmetal = sound('Selected/DeathMetal', .13,
    layer('impact', 'impactMetal_light_000.ogg', 0, .373, .8),
    layer('impact', 'impactPlate_light_001.ogg', 0, .50, .5, .065))
deathsoft = sound('Selected/DeathSoft', .13,
    layer('cardboard', 'impact.3.ogg', .342, .67), layer('rpg', 'cloth4.ogg', .028, .25, .3, .02))
on = sound('Selected/SwitchOn', .10, layer('rpg', 'metalLatch.ogg', .038, .22))
off = sound('Selected/SwitchOff', .10, layer('rpg', 'metalClick.ogg', .085, .32))
stone = sound('Selected/ImpactStone', .12, layer('rpg80', 'stones_04.ogg', .018, .383))
breakstone = sound('Selected/BreakStone', .14, layer('rpg80', 'stones_03.ogg', .033, .95))
breakwood = sound('Selected/BreakWood', .14, layer('wood', 'crack01.mp3.flac', .175, 1.0))
explosion = sound('Selected/Explosion', .17,
    layer('scifi', 'explosionCrunch_002.ogg', 0, 1.1),
    layer('rpg80', 'stones_04.ogg', .018, .383, .3, .025))

ENTRIES = []
def cue(name, clips, volume, low, high, reason):
    ENTRIES.append(dict(Cue=len(ENTRIES), Name=name, Clips=[str('Assets/_Project/Audio/SFX/'+c+'.wav') for c in clips],
                        Volume=volume, PitchMin=low, PitchMax=high, selection_reason=reason))
cue('Jump', [jump], .42, .98, 1.02, '平台跳跃原件的上扬短音，替换低沉布料摩擦；保留同一动作族。')
cue('AirJump', [jump], .40, 1.09, 1.13, '同源短上扬音，略升音高区分多段跳。')
cue('WallJump', [jump], .44, 1.03, 1.07, '同源弹起声，音高位于地面跳和多段跳之间。')
cue('Land', [land_a,land_b], .33, .98, 1.02, '换为真实鞋底脚步，辅以小比例纸板轻触起音；避免低频合成脚步。')
cue('Dash', [dash], .46, .98, 1.03, '选用皮带布条快速抽动的高频摩擦段，替换低沉长布料翻动。')
cue('SwordLight', [light_a,light_b], .50, .98, 1.04, '改用独立刀刃划动录音，两份短空气切割声；不再使用低频棒状破风。')
cue('SwordHeavy', [heavy_a,heavy_b], .58, .98, 1.02, '刀刃划动较完整的长弧线，靠时长与能量区分重击，避免降调变闷。')
cue('Hurt', [hurt_a,hurt_b], .56, .98, 1.02, '重新选真实非语音击打的短脆起音，替换低沉合成重拳，不含怪物叫声。')
cue('WheelOpen', [open_s], .36, 1, 1, '上扬界面提示，代替宽带开窗噪声。')
cue('WheelPreview', [preview], .20, 1, 1, '短拨弦式颗粒，连续扫选时轻且清楚。')
cue('WheelConfirm', [confirm], .42, 1, 1, '更明确的清亮确认短音，替换低音确认。')
cue('WheelCancel', [cancel], .32, 1, 1, '与展开配对的下降音。')
cue('WheelBack', [back], .28, 1, 1, '独立返回短点击，取消旧0.92降调。')
cue('WheelError', [error], .34, 1, 1, '可辨认的拒绝提示音型。')
cue('EnemyMelee', [light_a,light_b], .38, .98, 1.04, '沿用新刀刃破风族，敌人近战音量低于玩家。')
cue('EnemyShot', [shot], .32, .98, 1.03, '真正的短能量发射声，替换界面鼠标点击占位。')
cue('EnemyBeam', [beam], .36, .98, 1.02, '独立能量束素材，保留发射包络和高频电感。')
cue('EnemyHitWood', [wood_a,wood_b], .46, .98, 1.04, '真实干木触碰/短敲击，替换低沉合成木头撞击。')
cue('EnemyHitMetal', [metal_a,metal_b], .42, .98, 1.03, '小金属轻敲，保留本身的高频谐振。')
cue('EnemyHitSoft', [soft_a,soft_b], .46, .98, 1.04, '干燥纸板拍击的短啪声，避免毛绒被敲成金属或低频重锤。')
cue('EnemyDeathWood', [deathwood], .56, .98, 1.02, '真实木材断裂，有碎屑尾音，与单次受击分开。')
cue('EnemyDeathMetal', [deathmetal], .50, .98, 1.02, '轻金属和薄片碰落组成短散架声，保留清晰颗粒。')
cue('EnemyDeathSoft', [deathsoft], .54, .98, 1.03, '纸板拍击与短布料散落，保留柔软材质而不使用深低频重击。')
cue('SwitchOn', [on], .42, .99, 1.03, '真实金属搭扣起音，替换低沉锁具声。')
cue('SwitchOff', [off], .38, .99, 1.03, '另一份机械释放点击，不再把开启声降调复用。')
cue('ImpactStone', [stone], .46, .98, 1.03, '短硬石颗粒接触，替换较长的低调碎石声。')
cue('ImpactMetal', [metal_b,metal_a], .42, .98, 1.03, '轻金属碰撞，清晰但控制音量，避免连续碰撞刺耳。')
cue('ImpactWood', [wood_b,wood_a], .44, .98, 1.04, '短木块触碰，不再使用合成木重锤。')
cue('BreakStone', [breakstone], .58, .98, 1.02, '更完整的石屑崩裂段，替换原低调单块碰撞。')
cue('BreakWood', [breakwood], .56, .98, 1.02, '另一次真实木材断裂，保留较长的断裂尾音。')
cue('Explosion', [explosion], .60, .98, 1.02, '换另一份爆炸原件的爆裂起音与颗粒尾部，并加入少量石屑；无引信/燃烧声。')


def decode(path):
    return np.frombuffer(subprocess.check_output(['ffmpeg','-v','error','-i',str(path),'-ac','1',
                          '-ar',str(RATE),'-f','f32le','-']),dtype='<f4').astype(np.float64)


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    assert np.max(np.abs(data),initial=0) < 1, path
    with wave.open(str(path),'wb') as w:
        w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE)
        w.writeframes(np.round(data*32767).astype('<i2').tobytes())


def metrics(a):
    v=abs(np.fft.rfft(a))**2;f=np.fft.rfftfreq(len(a),1/RATE)
    return dict(duration=len(a)/RATE,peak=float(max(abs(a))),rms=float(np.sqrt(np.mean(a*a))),
                centroid_hz=float((v*f).sum()/v.sum()),above_3k_energy=float(v[(f>=3000)&(f<12000)].sum()/v.sum()))


def resolve(args, pack, filename):
    archive=args.sources/PACKS[pack][2]
    original=DOC/'SelectedOriginals'/pack/filename
    # Checked-in originals make the current selection reproducible without downloads.
    if not archive.exists() and original.exists():
        return original
    if archive.suffix=='.zip':
        with zipfile.ZipFile(archive) as z:
            names=[n for n in z.namelist() if Path(n).name==filename and not n.startswith('__')]
            if len(names)!=1:raise ValueError((pack,filename,names))
            data=z.read(names[0])
    else:
        unpack=args.sources/'selected-unpacked'/pack
        if not unpack.exists():
            subprocess.run(['7zz','x','-y','-o'+str(unpack),str(archive)],check=True,stdout=subprocess.DEVNULL)
        matches=list(unpack.rglob(filename))
        if len(matches)!=1:raise ValueError((pack,filename,matches))
        data=matches[0].read_bytes()
    original=DOC/'SelectedOriginals'/pack/filename
    original.parent.mkdir(parents=True,exist_ok=True);original.write_bytes(data)
    return original


def old_bank():
    # Read only. Serialized asset mutation is performed separately by Unity Editor.
    bank=(ROOT/'Project-Rule/Assets/_Project/Data/Audio/PlayerWheel_SfxBank.asset').read_text()
    guids={}
    for p in ASSETS.rglob('*.meta'):
        m=re.search(r'^guid: (\w+)',p.read_text(),re.M)
        if m:guids[m[1]]=Path(str(p)[:-5])
    result=[]
    for block in bank.split('  - Cue: ')[1:]:
        ids=re.findall(r'guid: (\w+)',block)
        volume=float(re.search(r'Volume: ([\d.]+)',block)[1])
        pitch=re.search(r'Pitch: \{x: ([\d.]+), y: ([\d.]+)',block)
        result.append((decode(guids[ids[0]]),volume,(float(pitch[1])+float(pitch[2]))/2))
    return result


def pitched(a, pitch):
    return np.interp(np.arange(0,len(a)-1,pitch),np.arange(len(a)),a)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--sources',type=Path,default=Path('/tmp/rule-audio-sources'))
    p.add_argument('--install',action='store_true')
    args=p.parse_args()
    before=old_bank() if args.install else None
    rendered={};records=[]
    for name,recipe in RECIPES.items():
        layers=[];origin=[]
        for spec in recipe['layers']:
            original=resolve(args,spec['pack'],spec['source'])
            full=decode(original);a=full[round(spec['start']*RATE):round(spec['end']*RATE)].copy()
            # Relative layer gain after matching raw active RMS; gain only, no EQ.
            a*=spec['gain']*.12/max(np.sqrt(np.mean(a*a)),1e-9)
            start=round(spec['at']*RATE);layers.append((start,a))
            origin.append(dict(**spec,original=str(original.relative_to(ROOT)),original_sha256=hashlib.sha256(original.read_bytes()).hexdigest(),
                               author=PACKS[spec['pack']][0],source_page=PACKS[spec['pack']][1],license='CC0-1.0'))
        length=max(s+len(a) for s,a in layers);out=np.zeros(length)
        for s,a in layers:out[s:s+len(a)]+=a
        # 0.3ms de-click; preserve onset. Short tail fade, not an EQ/filter.
        n=round(.0003*RATE);out[:n]*=np.linspace(0,1,n)
        n=min(round(.012*RATE),len(out)//4);out[-n:]*=np.linspace(1,0,n)
        gain=min(recipe['rms']/np.sqrt(np.mean(out*out)),.82/max(abs(out)))
        out*=gain
        target=PREVIEW/'Clips'/(name+'.wav');write(target,out);rendered[name]=out
        runtime=ASSETS/(name+'.wav')
        if args.install:runtime.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(target,runtime)
        records.append(dict(file=str(runtime.relative_to(ROOT)),sha256=hashlib.sha256(target.read_bytes()).hexdigest(),
                            processing='source excerpts + stated foley layers; gain balance; 0.3ms de-click; 12ms tail fade; mono PCM16 44100Hz; no EQ; no baked pitch shift',
                            sources=origin,metrics=metrics(out)))
    timeline={}
    previous_timeline=PREVIEW/'Timeline.json'
    if previous_timeline.exists():
        historic=json.loads(previous_timeline.read_text()).get('BeforeAfter')
        if historic:timeline['BeforeAfter']=historic
    groups=[('Player',range(0,8)),('Wheel',range(8,14)),('World',range(14,31))]
    for title,indices in groups:
        chunks=[np.zeros(round(.2*RATE))];rows=[];pos=.2
        for i in indices:
            e=ENTRIES[i];name=e['Clips'][0].split('Audio/SFX/')[1][:-4]
            a=pitched(rendered[name],(e['PitchMin']+e['PitchMax'])/2)*e['Volume']*.85
            rows.append(dict(time=round(pos,2),cue=e['Name']));chunks.extend([a,np.zeros(round(.4*RATE))]);pos+=len(a)/RATE+.4
            write(PREVIEW/'Events'/(e['Name']+'.wav'),a)
        write(PREVIEW/(title+'.wav'),np.concatenate(chunks));timeline[title]=rows
    if before:
        chunks=[];rows=[];pos=0
        for i in [3,5,7,17,18,23,28,30]:
            e=ENTRIES[i];name=e['Clips'][0].split('Audio/SFX/')[1][:-4]
            old,v,pitch=before[i];a=pitched(old,pitch)*v*.85
            b=pitched(rendered[name],(e['PitchMin']+e['PitchMax'])/2)*e['Volume']*.85
            # Compare both at the quieter RMS: source timbre, not louder-is-better.
            target=min(np.sqrt(np.mean(a*a)),np.sqrt(np.mean(b*b)))
            for version,c in [('before',a),('selected',b)]:
                c=c*target/np.sqrt(np.mean(c*c));peak=max(abs(c))
                if peak>.9:c*=.9/peak
                rows.append(dict(time=round(pos,2),cue=e['Name'],version=version))
                chunks.extend([c,np.zeros(round(.45*RATE))]);pos+=len(c)/RATE+.45
        write(PREVIEW/'BeforeAfter.wav',np.concatenate(chunks));timeline['BeforeAfter']=rows
    PREVIEW.mkdir(parents=True,exist_ok=True)
    (PREVIEW/'Timeline.json').write_text(json.dumps(timeline,ensure_ascii=False,indent=2)+'\n')
    manifest=dict(version=1,license='CC0-1.0',selection='Source-material redesign, not an EQ pass.',
                  retrieved='2026-10-18',entries=ENTRIES,files=records)
    (DOC/'SelectedSfxManifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(dict(cues=len(ENTRIES),clips=len(records),installed=args.install,
                         total_seconds=sum(r['metrics']['duration'] for r in records)),ensure_ascii=False))

if __name__=='__main__':main()
