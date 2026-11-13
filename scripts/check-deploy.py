#!/usr/bin/env python3
"""Check the rendered Kubernetes manifests keep the safeguards ADR 0002 relies on.

    kubectl kustomize --load-restrictor LoadRestrictionsNone deploy | python3 scripts/check-deploy.py

Fails, naming each problem, if the service's containers lack a probe, CPU and memory requests or limits,
a non-root user, a read-only filesystem or dropped privileges; if its pods mount a Kubernetes API token;
if the autoscaler is missing or allows more replicas than the API was sized for; if the namespace does not
enforce the restricted pod security standard; or if an API key appears in any manifest.
"""
import re
import sys

import yaml

docs = [d for d in yaml.safe_load_all(sys.stdin) if d]
problems = []


def find(kind, name):
    return next((d for d in docs if d['kind'] == kind and d['metadata']['name'] == name), None)


if re.search(r'sk-ant-[A-Za-z0-9_-]{8,}', yaml.safe_dump_all(docs)):
    problems.append('a manifest contains what looks like an Anthropic API key')

deployment = find('Deployment', 'intake')
if deployment is None:
    problems.append('there is no intake Deployment')
else:
    pod = deployment['spec']['template']['spec']
    if pod.get('automountServiceAccountToken') is not False:
        problems.append('the intake pods mount a Kubernetes API token they do not need')
    if not pod.get('securityContext', {}).get('runAsNonRoot'):
        problems.append('the intake pods may run as root')
    for c in pod['containers']:
        for probe in ('startupProbe', 'readinessProbe', 'livenessProbe'):
            if probe not in c:
                problems.append(f"container {c['name']} has no {probe}")
        resources = c.get('resources', {})
        for part in ('requests', 'limits'):
            if not {'cpu', 'memory'} <= set(resources.get(part, {})):
                problems.append(f"container {c['name']} lacks cpu and memory {part}")
        sc = c.get('securityContext', {})
        if not sc.get('readOnlyRootFilesystem'):
            problems.append(f"container {c['name']} can write to its own filesystem")
        if sc.get('allowPrivilegeEscalation') is not False or 'ALL' not in sc.get('capabilities', {}).get('drop', []):
            problems.append(f"container {c['name']} keeps privileges it does not need")
        for env in c.get('env', []):
            if env['name'] == 'ANTHROPIC_API_KEY' and 'value' in env:
                problems.append('the API key is set as a literal value, not read from a Secret')

hpa = find('HorizontalPodAutoscaler', 'intake')
if hpa is None or not (1 <= hpa['spec'].get('minReplicas', 0) <= hpa['spec']['maxReplicas'] <= 3):
    problems.append('the autoscaler is missing, or allows more than the 3 replicas the API was sized for')

namespace = find('Namespace', 'ticketdesk') or {}
if namespace.get('metadata', {}).get('labels', {}).get('pod-security.kubernetes.io/enforce') != 'restricted':
    problems.append('the ticketdesk namespace does not enforce the restricted pod security standard')

for p in problems:
    print(p, file=sys.stderr)
sys.exit(1 if problems else 0)
