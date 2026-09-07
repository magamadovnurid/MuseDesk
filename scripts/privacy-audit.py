"""Release gate. Reports paths/rules only, never matching secret values."""
import re, subprocess, sys
def git(*args): return subprocess.check_output(['git',*args])
failures=[]
for entry in git('ls-files','-z').decode().split('\0'):
    if not entry: continue
    if re.search(r'(^|/)(\.env(?:\..*)?|history\.json.*|chats\.json.*|connection\.json|permissions\.json|private)(/|$)|\.(pfx|p12|pem|key|museconnection)$',entry) and not entry.endswith('.env.example'):
        failures.append('Private file tracked: '+entry)
allowed={'codex@users.noreply.github.com','noreply@github.com','musedesk@users.noreply.github.com'}
for email in set(git('log','--all','--format=%ae%n%ce').decode().splitlines()):
    if email.lower() not in allowed: failures.append('Personal author/committer email in history (value redacted)')
print('\n'.join(failures) if failures else 'Privacy metadata gate passed: tracked paths and full author history')
sys.exit(bool(failures))
