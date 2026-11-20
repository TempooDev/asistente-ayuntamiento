"""RAGAS judge backed by the Anthropic API (used with `score.py --judge-provider anthropic`).

RAGAS only needs `generate_text` / `agenerate_text` returning a LangChain
`LLMResult`; this wrapper sends each RAGAS prompt as one user message and
returns the text of the reply. No sampling parameters are sent (current Claude
models reject them); the amount of reasoning is set with `effort`. Token usage
is accumulated so the cost of a scoring run can be reported.
"""
from __future__ import annotations

import os
import threading
import typing as t
from dataclasses import dataclass, field
from pathlib import Path

from langchain_core.outputs import Generation, LLMResult
from ragas.llms.base import BaseRagasLLM

# USD per million tokens (input, output)
PRICES = {"claude-sonnet-5-5": (2.0, 10.0), "claude-opus-5-5": (4.0, 20.0), "claude-haiku-5-5": (0.10, 0.50)}


def api_key() -> str:
    key = os.environ.get("ANTHROPIC_API_KEY")
    if not key:
        path = Path.home() / "tfg-eval" / "anthropic.key"
        if path.exists():
            key = path.read_text(encoding="utf-8").strip()
    if not key:
        raise RuntimeError("falta ANTHROPIC_API_KEY (o ~/tfg-eval/anthropic.key)")
    return key


@dataclass
class AnthropicRagasLLM(BaseRagasLLM):
    model: str = "claude-sonnet-5-5"
    effort: str = "low"
    max_tokens: int = 8000
    usage: dict = field(default_factory=lambda: {"input_tokens": 0, "output_tokens": 0, "calls": 0})

    def __post_init__(self):
        super().__post_init__()
        import anthropic
        self._sync = anthropic.Anthropic(api_key=api_key(), max_retries=4)
        self._async = anthropic.AsyncAnthropic(api_key=api_key(), max_retries=4)
        self._lock = threading.Lock()

    def _params(self, prompt) -> dict:
        return {"model": self.model, "max_tokens": self.max_tokens,
                "output_config": {"effort": self.effort},
                "messages": [{"role": "user", "content": prompt.to_string()}]}

    def _result(self, resp) -> LLMResult:
        with self._lock:
            self.usage["input_tokens"] += resp.usage.input_tokens
            self.usage["output_tokens"] += resp.usage.output_tokens
            self.usage["calls"] += 1
        text = "".join(b.text for b in resp.content if b.type == "text")
        return LLMResult(generations=[[Generation(text=text, generation_info={"stop_reason": resp.stop_reason})]])

    def generate_text(self, prompt, n: int = 1, temperature: float = 0.01,
                      stop: t.Optional[t.List[str]] = None, callbacks=None) -> LLMResult:
        return self._result(self._sync.messages.create(**self._params(prompt)))

    async def agenerate_text(self, prompt, n: int = 1, temperature: t.Optional[float] = 0.01,
                             stop: t.Optional[t.List[str]] = None, callbacks=None) -> LLMResult:
        return self._result(await self._async.messages.create(**self._params(prompt)))

    def is_finished(self, response: LLMResult) -> bool:
        return all(g.generation_info.get("stop_reason") == "end_turn" for gs in response.generations for g in gs)

    def cost_usd(self) -> float:
        p_in, p_out = PRICES.get(self.model, (0.0, 0.0))
        return self.usage["input_tokens"] / 1e6 * p_in + self.usage["output_tokens"] / 1e6 * p_out
