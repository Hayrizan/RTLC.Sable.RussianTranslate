"""Organize only untouched '# Сцена:' blocks using the game's compiled Ink graph.

Writes a separate candidate file; never overwrites the installed translation.
All context/pair lines and all retained bytes are checked before output.
"""
import argparse
import collections
import hashlib
import json
import re
from pathlib import Path
import UnityPy


def pair(line):
    match = re.search(r'(?<!\\)=', line)
    return (line[:match.start()], line[match.end():]) if match else None


def entries(lines):
    result = {}
    pending = None
    for line in lines:
        text = line.rstrip('\r\n')
        if text.startswith('#@'):
            assert pending is None, pending
            pending = text[2:]
        elif pending and not (not text or text.startswith('#') or
                               re.match(r'^\[.*\]$|^===.*===$', text)):
            assert pair(text), (pending, text)
            assert pending not in result, pending
            result[pending] = text
            pending = None
    assert pending is None
    return result


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('source', type=Path)
    ap.add_argument('game', type=Path)
    ap.add_argument('output', type=Path)
    ap.add_argument('--refine', action='store_true', help='Only replace the previously generated bottom section.')
    args = ap.parse_args()
    raw = args.source.read_bytes()
    text = raw.decode('utf-8-sig')
    lines = text.splitlines(keepends=True)
    before = entries(lines)
    env = UnityPy.load(str(args.game / 'Sable_Data/data.unity3d'))
    story = json.loads(next(o.read().m_Script for o in env.objects
                           if o.type.name == 'TextAsset' and o.read().m_Name == 'sable').lstrip('\ufeff'))
    root = story['root'][2]
    nodes, virtual, paths, parents = {}, {}, {}, {}

    def index(node, path, vpath, parent=None):
        nodes[path] = node
        virtual[vpath] = path
        paths[path] = vpath
        parents[path] = parent
        if isinstance(node, list):
            for i, child in enumerate(node):
                index(child, path + '/' + str(i), vpath + (str(i),), path)
                if isinstance(child, dict):
                    for k, value in child.items():
                        if isinstance(value, list):
                            index(value, path + '/' + str(i) + '/' + k, vpath + (k,), path)
        elif isinstance(node, dict):
            for k, child in node.items():
                if not isinstance(child, list):
                    index(child, path + '/' + k, vpath + (k,), path)

    for name, node in root.items():
        if isinstance(node, list): index(node, '/root/2/' + name, (name,))

    def resolve(target, source):
        if target.startswith('.'):
            out = list(paths[source])
            for part in target.split('.')[1:]:
                if part == '^':
                    if not out: return None
                    out.pop()
                elif part: out.append(part)
        else: out = target.split('.')
        return virtual.get(tuple(out))

    def descendants(path):
        return [p for p in nodes if p.startswith(path + '/')]

    # Resolve SpeakerName literals and speaker_* variables from actual Ink.
    variables = {}
    declarations = root['global decl']
    for i, value in enumerate(declarations):
        if isinstance(value, dict) and 'VAR=' in value:
            prev = declarations[max(0, i-4):i]
            literals = [v[1:] for v in prev if isinstance(v, str) and v.startswith('^')]
            if literals: variables[value['VAR=']] = literals[-1]
    translated = {}
    for value in before.values():
        en, ru = pair(value)
        translated.setdefault(en, ru)
    speakers = {}
    speaker_changes = collections.defaultdict(list)
    speaker_labels = set()
    for path, node in nodes.items():
        if not isinstance(node, list): continue
        for i, value in enumerate(node):
            if isinstance(value, dict) and value.get('VAR=') == 'SpeakerName':
                prev = node[max(0, i-6):i]
                names = [v[1:] for v in prev if isinstance(v, str) and v.startswith('^')]
                names += [variables[v['VAR?']] for v in prev if isinstance(v, dict) and v.get('VAR?') in variables]
                if names:
                    local_name = translated.get(names[-1], names[-1])
                    speakers.setdefault(path, local_name)
                    speaker_changes[path].append((i, local_name))
                    for j in range(max(0, i-6), i):
                        if isinstance(node[j], str) and node[j].startswith('^'):
                            speaker_labels.add(path + '/' + str(j))
    # Wrapper conversations often set the speaker then call a content knot.
    for _ in range(3):
        for path, name in list(speakers.items()):
            for p in descendants(path):
                node = nodes[p]
                if isinstance(node, dict):
                    target = node.get('->t->') or node.get('->') or node.get('^->')
                    if target and not target.startswith('.') and target.split('.')[0] in root and target != 'END_SCENE':
                        destination = resolve(target, p)
                        if destination: speakers.setdefault(destination, name)
        for scene in root:
            base = '/root/2/' + scene
            names = {name for path, name in speakers.items() if path.startswith(base + '/')}
            if len(names) == 1 and ('content' not in scene): speakers.setdefault(base, next(iter(names)))

    def speaker(path):
        matches = [p for p in speakers if path == p or path.startswith(p + '/')]
        if not matches: return 'Собеседник'
        scope = max(matches, key=len)
        if scope in speaker_changes and path != scope:
            part = path[len(scope)+1:].split('/')[0]
            if part.isdigit():
                prior = [name for i, name in speaker_changes[scope] if i <= int(part)]
                if prior: return prior[-1]
        return speakers[scope]

    # Named episodes identify these speakers even when SpeakerName is supplied
    # by scene code rather than assigned in the Ink knot.
    for episode, name in {'garay_arrested': 'Гарай', 'hamza_arrested': 'Хамза', 'yannis_arrested': 'Яннис'}.items():
        speakers['/root/2/atomic_heart_content/1/' + episode] = name
    for episode in ['jadi', 'driss', 'hilal', 'sizo', 'saima', 'elaria', 'zeki', 'umar']:
        original_name = variables.get('speaker_' + episode)
        if original_name:
            speakers['/root/2/the_final_mask_content/1/' + episode] = translated.get(original_name, original_name)
    speakers['/root/2/the_final_mask_content/1/the_gliding_message_from_jadi'] = 'Джади'
    speakers['/root/2/bones_of_rohana_content'] = 'Пастух'
    speakers['/root/2/a_shepherd_without_a_train_rumours_content'] = 'Опрашиваемый житель'

    # Only plain auto-generated scene blocks are eligible. All manual headings,
    # labels, notes and sections remain byte-for-byte in their current location.
    headers = [i for i, line in enumerate(lines) if line.startswith('# Сцена:') or
               (line.startswith('##') and not line.startswith('#@'))]
    movable, spans, scenes = {}, [], collections.OrderedDict()
    dialogue_scenes = {c.split('/')[3] for c, v in before.items()
                      if c.startswith('ink:/root/2/') and pair(v)[0].startswith('<<"')}
    for h, start in enumerate(headers):
        if args.refine: break
        if not lines[start].startswith('# Сцена:'): continue
        end = headers[h+1] if h+1 < len(headers) else len(lines)
        block = lines[start:end]
        name = lines[start].strip().split(': ', 1)[1]
        if name not in root: continue
        if any(re.match(r'^\[|^===', s) for s in block): continue
        if any(s.strip() and not s.startswith('#@') and not pair(s.rstrip('\r\n'))
               for s in block[1:]): continue
        if re.search(r'debug|definition|diary|recordings|letter|reply|example|_log|global decl', name, re.I): continue
        block_entries = entries(block)
        if not block_entries: continue
        known_speaker = speaker('/root/2/' + name) != 'Собеседник'
        if not known_speaker and not any(c.split('/')[3] in dialogue_scenes for c in block_entries): continue
        selected = {c: v for c, v in block_entries.items() if not re.search(
            r'debug|definition|diary|recordings|letter|reply|example|_log|global decl|signpost|postbox|puzzle|door_clue', c.split('/')[3], re.I)}
        if not selected: continue
        movable.update(selected)
        if len(selected) == len(block_entries): spans.append((start, end))
        else:
            for j in range(start+1, end):
                if lines[j].rstrip('\r\n').removeprefix('#@') in selected:
                    spans.append((j, j+2))
        for context, value in selected.items():
            actual_scene = context.split('/')[3]
            assert actual_scene in root, context
            scenes.setdefault(actual_scene, {})[context] = value

    if args.refine:
        marker = '### Диалоги — новые блоки для ручного размещения'
        start = next(i for i, line in enumerate(lines) if line.rstrip('\r\n') == marker)
        movable = entries(lines[start:])
        spans = [(start, len(lines))]
        for context, value in movable.items():
            assert context.startswith('ink:/root/2/'), context
            scenes.setdefault(context.split('/')[3], {})[context] = value

    output = ['### Диалоги — новые блоки для ручного размещения', '',
              '# Существовавшие переводы и метки сохранены. Ветки связаны по переходам Ink.',
              '# Условия и повторные обращения показаны отдельно; порядок их появления зависит от игры.', '']
    emitted = set()
    relevant = set()
    for context in movable:
        path = context[4:]
        while path:
            relevant.add(path)
            path = path.rpartition('/')[0]
    branch_count = 0
    unresolved = []
    choice_groups = {}
    merge_sources = collections.defaultdict(list)
    group_counter = 0
    completed_branches = collections.defaultdict(set)

    def merge_ready(target):
        return set(merge_sources.get(target, [])) <= completed_branches[target.split('/')[3]]

    def branch_exits(target):
        result = []
        if not target: return result
        for p in descendants(target):
            value = nodes[p]
            if isinstance(value, dict) and '->' in value and not value.get('c'):
                destination = resolve(value['->'], p)
                if destination and not destination.startswith(target + '/') and destination != target:
                    if destination not in result: result.append(destination)
        return result

    def show_merge(target):
        refs = list(dict.fromkeys(merge_sources.get(target, [])))
        if len(refs) > 1:
            output.extend(['=== ОБЪЕДИНЕНИЕ ВЕТОК ' + ', '.join(refs) + ': ОБЩЕЕ ПРОДОЛЖЕНИЕ ===', ''])
        elif refs:
            output.extend(['=== ПРОДОЛЖЕНИЕ ВЕТКИ ' + refs[0] + ' ===', ''])

    def emit(path, label):
        context = 'ink:' + path
        if context in movable and context not in emitted:
            emitted.add(context)
            output.extend(['[' + label + ']', '#@' + context, movable[context], ''])

    visited = set()
    def walk(path, branch=False):
        nonlocal branch_count, group_counter
        if path not in relevant: return
        if path in visited: return
        visited.add(path)
        node = nodes[path]
        if not isinstance(node, list): return
        if path.count('/') == 3:
            episodes = [(i, k) for i, v in enumerate(node) if isinstance(v, dict)
                        for k, child in v.items() if isinstance(child, list)]
            if any(k in ('quest_start', 'quest_start_regular') for _, k in episodes):
                for i, key in episodes:
                    target = path + '/' + str(i) + '/' + key
                    if target not in relevant: continue
                    title = {'quest_start': 'ПЕРВАЯ ВСТРЕЧА / НАЧАЛО ЗАДАНИЯ',
                             'quest_start_regular': 'ПЕРВАЯ ВСТРЕЧА',
                             'quest_start_alt': 'АЛЬТЕРНАТИВНОЕ НАЧАЛО',
                             'quest_active': 'ЗАДАНИЕ В ПРОЦЕССЕ',
                             'quest_in_progress': 'ЗАДАНИЕ В ПРОЦЕССЕ',
                             'quest_complete': 'ЗАВЕРШЕНИЕ ЗАДАНИЯ'}.get(key, 'ОТДЕЛЬНЫЙ ЭПИЗОД')
                    output.extend(['=== ' + title + ' (' + key + ') ===', ''])
                    walk(target)
                if any('ink:' + q in movable and 'ink:' + q not in emitted for q in descendants(path)):
                    output.extend(['=== ДРУГИЕ СОСТОЯНИЯ И ПОВТОРНЫЕ ОБРАЩЕНИЯ ===', ''])
        # Choice text is produced between str and /str, before a '*' command.
        choice_text, in_string = [], False
        choices = {}
        for i, value in enumerate(node):
            if value == 'str': in_string = True
            elif value == '/str': in_string = False
            elif in_string and isinstance(value, str) and value.startswith('^'):
                choice_text.append(path + '/' + str(i))
            elif isinstance(value, dict) and '*' in value:
                choices[i] = list(choice_text)
                choice_text = []
            elif isinstance(value, dict) and ('VAR=' in value or 'temp=' in value): choice_text = []
        choice_paths = {p for group in choices.values() for p in group}
        visible_choices = [i for i in choices if any('ink:' + p in movable for p in choices[i]) or
                           resolve(node[i]['*'], path + '/' + str(i)) in relevant]
        continuations = []
        if visible_choices:
            group_counter += 1
            group_number = group_counter
            choice_groups[path] = group_number
            for ordinal, i in enumerate(visible_choices, 1):
                target = resolve(node[i]['*'], path + '/' + str(i))
                for destination in branch_exits(target):
                    label = str(group_number) + '.' + str(ordinal)
                    if label not in merge_sources[destination]: merge_sources[destination].append(label)
                    if destination not in continuations: continuations.append(destination)
        for i, value in enumerate(node):
            p = path + '/' + str(i)
            if isinstance(value, str) and value.startswith('^'):
                if p in choice_paths: continue
                if p in speaker_labels: label = 'Имя собеседника'
                elif value.startswith('^<<"'): label = 'Реплика: ' + speaker(p)
                elif value.startswith('^<<'): label = 'Текст диалога'
                else: label = 'Нарратив Сейбл'
                emit(p, label)
            elif isinstance(value, dict) and '*' in value:
                target = resolve(value['*'], p)
                if not any('ink:' + q in movable for q in choices.get(i, [])) and target not in relevant: continue
                branch_count += 1
                tag = value['*'].split('.')[-1]
                number = str(group_number) + '.' + str(visible_choices.index(i) + 1)
                if i == visible_choices[0]: output.extend(['=== ВЫБОР ' + str(group_number) + ' ===', ''])
                output.append('=== ВАРИАНТ ВЕТКИ ' + number + ' (' + tag + ') ===')
                for q in choices.get(i, []): emit(q, 'Выбор Сейбл: ветка ' + number)
                if target:
                    output.append('=== ОТВЕТ И ПРОДОЛЖЕНИЕ ВЕТКИ ' + number + ' ===')
                    before_branch = len(emitted)
                    walk(target, True)
                    completed_branches[path.split('/')[3]].add(number)
                    if len(emitted) == before_branch:
                        output.extend(['# Отдельной переводимой реплики нет: переход к общему продолжению или завершение.', ''])
                    for destination in branch_exits(target):
                        if destination in relevant:
                            refs = list(dict.fromkeys(merge_sources[destination]))
                            if len(refs) > 1:
                                output.extend(['# Далее — общее продолжение веток ' + ', '.join(refs) + '.', ''])
                else: unresolved.append((p, value['*']))
            elif isinstance(value, list):
                walk(p)
        for destination in continuations:
            if (destination in relevant and destination not in visited and merge_ready(destination)
                    and destination.split('/')[3] == path.split('/')[3]):
                # Only array destinations can be emitted as a whole. Numeric
                # instruction destinations remain within their owning episode.
                if isinstance(nodes[destination], list):
                    show_merge(destination)
                    walk(destination)
        for i, value in enumerate(node):
            if not isinstance(value, dict): continue
            for key, child in value.items():
                if not isinstance(child, list): continue
                p = path + '/' + str(i) + '/' + key
                if p in visited: continue
                if p in merge_sources and not merge_ready(p): continue
                if not any('ink:' + q in movable for q in [p] + descendants(p)): continue
                if key.startswith('g-'): title = 'СХОЖДЕНИЕ ВЕТОК'
                elif key.startswith('c-'): title = 'ПРОДОЛЖЕНИЕ ВАРИАНТА'
                elif re.fullmatch(r's\d+', key): title = 'АЛЬТЕРНАТИВНЫЙ ФРАГМЕНТ'
                elif key == 'b': title = 'ФРАГМЕНТ ПО УСЛОВИЮ'
                else: title = 'ЭПИЗОД / ОБРАЩЕНИЕ'
                episode_titles = {'quest_start': 'НАЧАЛО ЗАДАНИЯ', 'quest_start_regular': 'ПЕРВАЯ ВСТРЕЧА',
                    'quest_start_alt': 'АЛЬТЕРНАТИВНОЕ НАЧАЛО', 'quest_active': 'ЗАДАНИЕ В ПРОЦЕССЕ',
                    'quest_in_progress': 'ЗАДАНИЕ В ПРОЦЕССЕ', 'quest_complete': 'ЗАВЕРШЕНИЕ ЗАДАНИЯ',
                    'post_quest': 'ПОСЛЕ ЗАДАНИЯ', 'after_quest_complete': 'ПОСЛЕ ЗАДАНИЯ',
                    'generic_chat': 'ПОВТОРНЫЙ РАЗГОВОР', 'first_time': 'ПЕРВЫЙ РАЗГОВОР',
                    'repeat': 'ПОВТОРНОЕ ОБРАЩЕНИЕ', 'conversation_end': 'ПРОЩАНИЕ',
                    'begin_trading': 'ПЕРЕХОД К ТОРГОВЛЕ', 'start': 'НАЧАЛО РАЗГОВОРА', 'end': 'ЗАВЕРШЕНИЕ'}
                title = episode_titles.get(key, title)
                if p in merge_sources: show_merge(p)
                else: output.extend(['=== ' + title + ' (' + key + ') ===', ''])
                walk(p)

    groups = collections.OrderedDict()
    for scene in scenes:
        name = speaker('/root/2/' + scene)
        if name == 'Собеседник':
            names = list(dict.fromkeys(speaker(c[4:]) for c in scenes[scene] if speaker(c[4:]) != 'Собеседник'))
            if names: name = ' / '.join(names)
        groups.setdefault(name if name != 'Собеседник' else 'Диалог: ' + scene, []).append(scene)
    for name, group in groups.items():
        output.extend(['## ' + name, ''])
        for scene in group:
            group_counter = 0
            output.extend(['=== НАЧАЛО / УСЛОВИЯ ДИАЛОГА (' + scene + ') ===', ''])
            walk('/root/2/' + scene)
            leftovers = set(scenes[scene]) - emitted
            if leftovers:
                raise AssertionError(('Unclassified entries', scene, sorted(leftovers)[:10]))
            output.extend(['=== КОНЕЦ БЛОКА (' + scene + ') ===', ''])
    assert not unresolved, unresolved[:10]
    assert emitted == set(movable)
    removed = {i for a, b in spans for i in range(a, b)}
    retained = ''.join(line for i, line in enumerate(lines) if i not in removed)
    newline = '\r\n' if '\r\n' in text else '\n'
    final = retained + ('' if args.refine else newline * 2) + newline.join(output)
    assert entries(final.splitlines(keepends=True)) == before
    assert final.startswith(retained)
    args.output.write_bytes((b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b'') + final.encode('utf-8'))
    report = {'source_sha256': hashlib.sha256(raw).hexdigest(), 'entries_before_after': len(before),
              'moved_entries': len(emitted), 'scene_blocks': len(spans), 'scenes': len(scenes),
              'groups': {k: v for k, v in groups.items()}, 'choice_links': branch_count,
              'retained_bytes': len(retained.encode('utf-8')), 'unresolved': unresolved}
    args.output.with_suffix('.report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == '__main__': main()
