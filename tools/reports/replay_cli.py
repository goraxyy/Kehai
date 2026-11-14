"""Render one or two actual shift recordings to a standalone private replay file."""
import argparse
from .common import atomic_write
from .replay import load_record, render


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('recordings', nargs='+')
    ap.add_argument('--decisions', action='append', default=[])
    ap.add_argument('--model', action='append', default=[])
    ap.add_argument('--out', required=True)
    a = ap.parse_args(argv)
    try:
        if not 1 <= len(a.recordings) <= 2 or len(a.decisions) > len(a.recordings) or len(a.model) > len(a.recordings):
            raise ValueError('one or two recordings; optional sidecars/models in the same order')
        records = [load_record(p, a.decisions[i] if i < len(a.decisions) else None, a.model[i] if i < len(a.model) else None) for i, p in enumerate(a.recordings)]
        atomic_write(a.out, render(records))
        print(a.out)
    except (ValueError, OSError, KeyError) as ex:
        ap.error(str(ex))


if __name__ == '__main__':
    main()
