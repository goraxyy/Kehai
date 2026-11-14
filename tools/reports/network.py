"""Exact feed-forward Swish export inspection. No fabricated decision attribution.

The supported derivative is local sensitivity of ONE recorded head logit to each input.
It is neither a causal explanation nor a masked action probability derivative.
"""
import math
from .common import digest


def sigmoid(x):
    if x >= 0:
        return 1 / (1 + math.exp(-x))
    z = math.exp(x)
    return z / (1 + z)


def inspect(net, inputs, branch, choice):
    if net.get('kind') != 'brain' or net.get('recurrent', False):
        raise ValueError('only exported feed-forward brain parts supported; recurrent state is unavailable')
    if type(branch) is not int or type(choice) is not int or branch < 0 or choice < 0:
        raise ValueError('branch and choice must be nonnegative integer indices')
    mean, std = net['mean'], net['std']
    if len(inputs) != len(mean) or len(std) != len(mean):
        raise ValueError('input/normalizer shape mismatch')
    if not all(type(x) in (int, float) and math.isfinite(x) for x in [*inputs, *mean, *std]):
        raise ValueError('nonfinite input/normalizer')
    scales = [v if v > 1e-8 else 1 for v in std]
    raw = [(x-m)/s for x, m, s in zip(inputs, mean, scales)]
    if any(abs(x) == 5 for x in raw):
        raise ValueError('saliency unavailable at a nondifferentiable normalizer clamp boundary')
    h = [max(-5, min(5, x)) for x in raw]
    layers, pre = [h], []
    for layer in net['hidden']:
        n, weights, bias = layer['out'], layer['w'], layer['b']
        if len(weights) != n*len(h) or len(bias) != n:
            raise ValueError('hidden layer shape mismatch')
        z = [sum(weights[i*len(h)+j]*v for j, v in enumerate(h)) + bias[i] for i in range(n)]
        pre.append(z)
        h = [x*sigmoid(x) for x in z]
        layers.append(h)
    head = net['heads'][branch]
    if len(head['w']) != head['out']*len(h) or len(head['b']) != head['out'] or not 0 <= choice < head['out']:
        raise ValueError('head shape or choice mismatch')
    logits = [sum(head['w'][i*len(h)+j]*v for j, v in enumerate(h))+head['b'][i] for i in range(head['out'])]
    grad = head['w'][choice*len(h):(choice+1)*len(h)]
    for layer, z, previous in reversed(list(zip(net['hidden'], pre, layers[:-1]))):
        dz = [g*(sigmoid(x)+x*sigmoid(x)*(1-sigmoid(x))) for g, x in zip(grad, z)]
        grad = [sum(dz[i]*layer['w'][i*len(previous)+j] for i in range(layer['out'])) for j in range(len(previous))]
    grad = [g/s if -5 < x < 5 else 0 for g, s, x in zip(grad, scales, raw)]
    top = sorted(range(len(grad)), key=lambda i: abs(grad[i]), reverse=True)[:3]
    if not all(math.isfinite(x) for x in [*logits, *grad, *(v for layer in layers for v in layer)]):
        raise ValueError('nonfinite model arithmetic')
    return {'layers': layers, 'logits': logits, 'gradient': grad,
            'top_three': [{'input': i, 'value': inputs[i], 'd_logit_d_input': grad[i]} for i in top],
            'method': 'analytic local derivative of recorded chosen head logit; absolute derivative rank; not causal influence or legal-pair probability'}
